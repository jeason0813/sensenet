﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Lucene.Net.Index;
using System.Data.Common;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SenseNet.Diagnostics;

namespace SenseNet.Search.Indexing
{
    public enum IndexDifferenceKind { NotInIndex, NotInDatabase, MoreDocument, DifferentNodeTimestamp, DifferentVersionTimestamp, DifferentLastPublicFlag, DifferentLastDraftFlag }

    [DebuggerDisplay("{Kind} VersionId: {VersionId}, DocId: {DocId}")]
    [Serializable]
    public class Difference
    {
        public Difference(IndexDifferenceKind kind)
        {
            this.Kind = kind;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public IndexDifferenceKind Kind { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public int DocId { get; internal set; }
        public int VersionId { get; internal set; }
        public long DbNodeTimestamp { get; internal set; }
        public long DbVersionTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public long IxNodeTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public long IxVersionTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public int NodeId { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public string Path { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public string Version { get; internal set; }

        /// <summary>
        /// Not used used if the Kind is other than DifferentVersionFlag
        /// </summary>
        public bool IsLastPublic { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is other than DifferentVersionFlag
        /// </summary>
        public bool IsLastDraft { get; internal set; }

        public override string ToString()
        {
            if (this.Kind == IndexDifferenceKind.NotInIndex)
            {
                var head = NodeHead.GetByVersionId(this.VersionId);
                if (head != null)
                {
                    var versionNumber = head.Versions
                        .Where(x => x.VersionId == this.VersionId)
                        .Select(x => x.VersionNumber)
                        .FirstOrDefault()
                        ?? new VersionNumber(0, 0);
                    this.Path = head.Path;
                    this.NodeId = head.Id;
                    this.Version = versionNumber.ToString();
                }
            }

            var sb = new StringBuilder();
            sb.Append(Kind).Append(": ");
            if (DocId >= 0)
                sb.Append("DocId: ").Append(DocId).Append(", ");
            if (VersionId > 0)
                sb.Append("VersionId: ").Append(VersionId).Append(", ");
            if (NodeId > 0)
                sb.Append("NodeId: ").Append(NodeId).Append(", ");
            if (Version != null)
                sb.Append("Version: ").Append(Version).Append(", ");
            if (DbNodeTimestamp > 0)
                sb.Append("DbNodeTimestamp: ").Append(DbNodeTimestamp).Append(", ");
            if (IxNodeTimestamp > 0)
                sb.Append("IxNodeTimestamp: ").Append(IxNodeTimestamp).Append(", ");
            if (DbVersionTimestamp > 0)
                sb.Append("DbVersionTimestamp: ").Append(DbVersionTimestamp).Append(", ");
            if (IxVersionTimestamp > 0)
                sb.Append("IxVersionTimestamp: ").Append(IxVersionTimestamp).Append(", ");
            if (Path != null)
                sb.Append("Path: ").Append(Path);
            return sb.ToString();
        }
    }

    public class IntegrityChecker
    {
        [SenseNet.ApplicationModel.ODataFunction]
        public static object CheckIndexIntegrity(SenseNet.ContentRepository.Content content, bool recurse)
        {
            var path = content == null ? null : content.Path;

            CompletionState completionState;
            using (var readerFrame = LuceneManager.GetIndexReaderFrame())
                completionState = CompletionState.ParseFromReader(readerFrame.IndexReader);
            var lastDatabaseId = LuceneManager.GetLastStoredIndexingActivityId();

            var channel = SenseNet.ContentRepository.DistributedApplication.ClusterChannel;
            var appDomainName = channel == null ? null : channel.ReceiverName;

            return new
            {
                AppDomainName = appDomainName,
                LastStoredActivity = lastDatabaseId,
                LastProcessedActivity = completionState.LastActivityId,
                GapsLength = completionState.GapsLength,
                Gaps = completionState.Gaps,
                Differences = Check(path, recurse)
            };
        }

        public static IEnumerable<Difference> Check()
        {
            return Check(null, true);
        }
        public static IEnumerable<Difference> Check(string path, bool recurse)
        {
            if (recurse)
            {
                if (path != null)
                    if (path.ToLower() == SenseNet.ContentRepository.Repository.RootPath.ToLower())
                        path = null;
                return new IntegrityChecker().CheckRecurse(path);
            }
            return new IntegrityChecker().CheckNode(path ?? SenseNet.ContentRepository.Repository.RootPath);
        }

        /*==================================================================================== Instance part */

        private IEnumerable<Difference> CheckNode(string path)
        {
            var result = new List<Difference>();
            using (var readerFrame = LuceneManager.GetIndexReaderFrame())
            {
                var ixreader = readerFrame.IndexReader;
                var docids = new List<int>();
                var proc = DataProvider.Current.GetTimestampDataForOneNodeIntegrityCheck(path, GetExcludedNodeTypeIds());
                using (var dbreader = proc.ExecuteReader())
                {
                    while (dbreader.Read())
                    {
                        var docid = CheckDbAndIndex(dbreader, ixreader, result);
                        if (docid >= 0)
                            docids.Add(docid);
                    }
                }
                var scoredocs = GetDocsUnderTree(path, false);
                foreach (var scoredoc in scoredocs)
                {
                    var docid = scoredoc.Doc;
                    var doc = ixreader.Document(docid);
                    if (!docids.Contains(docid))
                    {
                        result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                        {
                            DocId = scoredoc.Doc,
                            VersionId = ParseInt(doc.Get(LucObject.FieldName.VersionId)),
                            NodeId = ParseInt(doc.Get(LucObject.FieldName.NodeId)),
                            Path = path,
                            Version = doc.Get(LucObject.FieldName.Version),
                            IxNodeTimestamp = ParseLong(doc.Get(LucObject.FieldName.NodeTimestamp)),
                            IxVersionTimestamp = ParseLong(doc.Get(LucObject.FieldName.VersionTimestamp))
                        });
                    }
                }
            }
            return result;
        }

        private int intsize = sizeof(int) * 8;
        private int numdocs;
        private int[] docbits;
        private IEnumerable<Difference> CheckRecurse(string path)
        {
            var result = new List<Difference>();

            using (var op = SnTrace.Index.StartOperation("Index Integrity Checker: CheckRecurse {0}", path))
            {
                using (var readerFrame = LuceneManager.GetIndexReaderFrame())
                {
                    var ixreader = readerFrame.IndexReader;
                    numdocs = ixreader.NumDocs() + ixreader.NumDeletedDocs();
                    var x = numdocs / intsize;
                    var y = numdocs % intsize;
                    docbits = new int[x + (y > 0 ? 1 : 0)];
                    if (path == null)
                    {
                        if (y > 0)
                        {
                            var q = 0;
                            for (int i = 0; i < y; i++)
                                q += 1 << i;
                            docbits[docbits.Length - 1] = q ^ (-1);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < docbits.Length; i++)
                            docbits[i] = -1;
                        var scoredocs = GetDocsUnderTree(path, true);
                        for (int i = 0; i < scoredocs.Length; i++)
                        {
                            var docid = scoredocs[i].Doc;
                            docbits[docid / intsize] ^= 1 << docid % intsize;
                        }
                    }
                    var proc = DataProvider.Current.GetTimestampDataForRecursiveIntegrityCheck(path, GetExcludedNodeTypeIds());
                    var progress = 0;
                    using (var dbreader = proc.ExecuteReader())
                    {
                        while (dbreader.Read())
                        {
                            if ((++progress % 10000) == 0)
                                SnTrace.Index.Write("Index Integrity Checker: CheckDbAndIndex: progress={0}/{1}, diffs:{2}", progress, numdocs, result.Count);

                            var docid = CheckDbAndIndex(dbreader, ixreader, result);
                            if (docid > -1)
                                docbits[docid / intsize] |= 1 << docid % intsize;
                        }
                    }
                    SnTrace.Index.Write("Index Integrity Checker: CheckDbAndIndex finished. Progress={0}/{1}, diffs:{2}", progress, numdocs, result.Count);
                    for (int i = 0; i < docbits.Length; i++)
                    {
                        if (docbits[i] != -1)
                        {
                            var bits = docbits[i];
                            for (int j = 0; j < intsize; j++)
                            {
                                if ((bits & (1 << j)) == 0)
                                {
                                    var docid = i * intsize + j;
                                    if (docid >= numdocs)
                                        break;
                                    if (!ixreader.IsDeleted(docid))
                                    {
                                        var doc = ixreader.Document(docid);
                                        result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                                        {
                                            DocId = docid,
                                            VersionId = ParseInt(doc.Get(LucObject.FieldName.VersionId)),
                                            NodeId = ParseInt(doc.Get(LucObject.FieldName.NodeId)),
                                            Path = doc.Get(LucObject.FieldName.Path),
                                            Version = doc.Get(LucObject.FieldName.Version),
                                            IxNodeTimestamp = ParseLong(doc.Get(LucObject.FieldName.NodeTimestamp)),
                                            IxVersionTimestamp = ParseLong(doc.Get(LucObject.FieldName.VersionTimestamp))
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                op.Successful = true;
            }
            return result.ToArray();
        }
        private int CheckDbAndIndex(DbDataReader dbreader, IndexReader ixreader, List<Difference> result)
        {
            var nodeIdFromDb = dbreader.GetInt32(0);
            var versionId = dbreader.GetInt32(1);
            var dbNodeTimestamp = dbreader.GetInt64(2);
            var dbVersionTimestamp = dbreader.GetInt64(3);
            var lastMajorVersionId = dbreader.IsDBNull(4) ? 0 : dbreader.GetInt32(4);
            var lastMinorVersionId = dbreader.IsDBNull(5) ? 0 : dbreader.GetInt32(5);

            var termDocs = ixreader.TermDocs(new Lucene.Net.Index.Term(LucObject.FieldName.VersionId, Lucene.Net.Util.NumericUtils.IntToPrefixCoded(versionId)));
            Lucene.Net.Documents.Document doc = null;
            int docid = -1;
            if (termDocs.Next())
            {
                docid = termDocs.Doc();
                doc = ixreader.Document(docid);
                var indexNodeTimestamp = ParseLong(doc.Get(LucObject.FieldName.NodeTimestamp));
                var indexVersionTimestamp = ParseLong(doc.Get(LucObject.FieldName.VersionTimestamp));
                var nodeId = ParseInt(doc.Get(LucObject.FieldName.NodeId));
                var version = doc.Get(LucObject.FieldName.Version);
                var p = doc.Get(LucObject.FieldName.Path);
                if (termDocs.Next())
                {
                    result.Add(new Difference(IndexDifferenceKind.MoreDocument)
                        {
                            DocId = docid,
                            NodeId = nodeId,
                            VersionId = versionId,
                            Version = version,
                            Path = p,
                            DbNodeTimestamp = dbNodeTimestamp,
                            DbVersionTimestamp = dbVersionTimestamp,
                            IxNodeTimestamp = indexNodeTimestamp,
                            IxVersionTimestamp = indexVersionTimestamp,
                        });
                }
                if (dbVersionTimestamp != indexVersionTimestamp)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentVersionTimestamp)
                    {
                        DocId = docid,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p
                    });
                }

                // Check version flags by comparing them to the db: we assume that the last
                // major and minor version ids in the Nodes table is the correct one.
                var isLastPublic = doc.Get(LucObject.FieldName.IsLastPublic);
                var isLastDraft = doc.Get(LucObject.FieldName.IsLastDraft);
                var isLastPublicInDb = versionId == lastMajorVersionId;
                var isLastDraftInDb = versionId == lastMinorVersionId;
                var isLastPublicInIndex = isLastPublic == BooleanIndexHandler.YES;
                var isLastDraftInIndex = isLastDraft == BooleanIndexHandler.YES;

                if (isLastPublicInDb != isLastPublicInIndex)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentLastPublicFlag)
                    {
                        DocId = docid,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p,
                        IsLastPublic = isLastPublicInIndex,
                        IsLastDraft = isLastDraftInIndex
                    });
                }
                if (isLastDraftInDb != isLastDraftInIndex)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentLastDraftFlag)
                    {
                        DocId = docid,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p,
                        IsLastPublic = isLastPublicInIndex,
                        IsLastDraft = isLastDraftInIndex
                    });
                }

                if (dbNodeTimestamp != indexNodeTimestamp)
                {
                    var ok = false;
                    if (isLastDraft != BooleanIndexHandler.YES)
                    {
                        var latestDocs = ixreader.TermDocs(new Lucene.Net.Index.Term(LucObject.FieldName.NodeId, Lucene.Net.Util.NumericUtils.IntToPrefixCoded(nodeId)));
                        Lucene.Net.Documents.Document latestDoc = null;
                        while (latestDocs.Next())
                        {
                            var latestdocid = latestDocs.Doc();
                            var d = ixreader.Document(latestdocid);
                            if (d.Get(LucObject.FieldName.IsLastDraft) != BooleanIndexHandler.YES)
                                continue;
                            latestDoc = d;
                            break;
                        }
                        var latestPath = latestDoc.Get(LucObject.FieldName.Path);
                        if (latestPath == p)
                            ok = true;
                    }
                    if (!ok)
                    {
                        result.Add(new Difference(IndexDifferenceKind.DifferentNodeTimestamp)
                        {
                            DocId = docid,
                            VersionId = versionId,
                            DbNodeTimestamp = dbNodeTimestamp,
                            DbVersionTimestamp = dbVersionTimestamp,
                            IxNodeTimestamp = indexNodeTimestamp,
                            IxVersionTimestamp = indexVersionTimestamp,
                            NodeId = nodeId,
                            Version = version,
                            Path = p
                        });
                    }
                }
            }
            else
            {
                result.Add(new Difference(IndexDifferenceKind.NotInIndex)
                {
                    DocId = docid,
                    VersionId = versionId,
                    DbNodeTimestamp = dbNodeTimestamp,
                    DbVersionTimestamp = dbVersionTimestamp,
                    NodeId = nodeIdFromDb
                });
            }
            return docid;
        }
        private ScoreDoc[] GetDocsUnderTree(string path, bool recurse)
        {
            var field = recurse ? "InTree" : "Path";
            var lq = LucQuery.Parse(String.Format("{0}:'{1}'", field, path.ToLower()));

            using (var readerFrame = LuceneManager.GetIndexReaderFrame())
            {
                var idxReader = readerFrame.IndexReader;
                var searcher = new IndexSearcher(idxReader);
                var numDocs = idxReader.NumDocs();
                try
                {
                    var collector = TopScoreDocCollector.Create(numDocs, false);
                    searcher.Search(lq.Query, collector);
                    var topDocs = collector.TopDocs(0, numDocs);
                    return topDocs.ScoreDocs;
                }
                finally
                {
                    if (searcher != null)
                        searcher.Close();
                    searcher = null;
                }
            }
        }

        private static int[] GetExcludedNodeTypeIds()
        {
            // We must exclude those content types from the integrity check
            // where indexing is completely switched OFF, because otherwise
            // these kinds of content would appear as missing items.
            return ContentType.GetContentTypes().Where(ct => !ct.IndexingEnabled)
                    .Select(ct => ActiveSchema.NodeTypes[ct.Name].Id).ToArray();
        }

        private static int ParseInt(string data)
        {
            Int32 result;
            if (Int32.TryParse(data, out result))
                return result;
            return -1;
        }
        private static long ParseLong(string data)
        {
            Int64 result;
            if (Int64.TryParse(data, out result))
                return result;
            return -1;
        }

    }
}
