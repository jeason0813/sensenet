using System;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using System.Collections.Generic;
using SenseNet.ContentRepository.Storage.Schema;
using System.Linq;
using SenseNet.Search;

namespace SenseNet.ContentRepository.Storage.Search
{
    [System.Diagnostics.DebuggerDisplay("{Name} = {Value}")]
    public class NodeQueryParameter
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    public static class NodeQuery
    {
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static int InstanceCount(NodeType nodeType, bool exactType)
        {
            return DataProvider.Current.InstanceCount(exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray());
        }

        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryChildren(string parentPath)
        {
            if (parentPath == null)
                throw new ArgumentNullException("parentPath");
            var head = NodeHead.Get(parentPath);
            if (head == null)
                throw new InvalidOperationException("Node does not exist: " + parentPath);

            var ids = DataProvider.Current.GetChildrenIdentfiers(head.Id);
            return new QueryResult(new NodeList<Node>(ids));
        }

        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryChildren(int parentId)
        {
            if (parentId <= 0)
                throw new InvalidOperationException("Parent node is not saved");

            var ids = DataProvider.Current.GetChildrenIdentfiers(parentId);
            return new QueryResult(new NodeList<Node>(ids));
        }

        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByPath(string pathStart, bool orderByPath)
        {
            if (pathStart == null)
                throw new ArgumentNullException("pathStart");
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByPath(pathStart, orderByPath);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByType(NodeType nodeType, bool exactType)
        {
            if (nodeType == null)
                throw new ArgumentNullException("nodeType");
            var typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByType(typeIds);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndName(NodeType nodeType, bool exactType, string name)
        {
            if (nodeType == null)
                throw new ArgumentNullException("nodeType");
            var typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByTypeAndPathAndName(typeIds, (string[])null, false, name);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndPath(NodeType nodeType, bool exactType, string pathStart, bool orderByPath)
        {
            if (nodeType == null)
                throw new ArgumentNullException("nodeType");
            if (pathStart == null)
                throw new ArgumentNullException("pathStart");
            var typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByTypeAndPath(typeIds, pathStart, orderByPath);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndPath(NodeType nodeType, bool exactType, string[] pathStart, bool orderByPath)
        {
            if (nodeType == null)
                throw new ArgumentNullException("nodeType");
            if (pathStart == null)
                throw new ArgumentNullException("pathStart");
            var typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByTypeAndPath(typeIds, pathStart, orderByPath);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndPathAndName(NodeType nodeType, bool exactType, string pathStart, bool orderByPath, string name)
        {
            int[] typeIds = null;
            if (nodeType != null)
                typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();
            IEnumerable<int> ids = DataProvider.Current.QueryNodesByTypeAndPathAndName(typeIds, pathStart, orderByPath, name);
            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndPathAndName(IEnumerable<NodeType> nodeTypes, bool exactType, string pathStart, bool orderByPath, string name)
        {
            int[] typeIds = null;
            if (nodeTypes != null)
            {
                var idList = new List<int>();

                if (exactType)
                {
                    idList = nodeTypes.Select(nt => nt.Id).ToList();
                }
                else
                {
                    foreach (var nodeType in nodeTypes)
                    {
                        idList.AddRange(nodeType.GetAllTypes().ToIdArray());
                    }
                }

                if (idList.Count > 0)
                    typeIds = idList.ToArray();
            }

            var ids = DataProvider.Current.QueryNodesByTypeAndPathAndName(typeIds, pathStart, orderByPath, name);
            return new QueryResult(new NodeList<Node>(ids));
        }

        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByTypeAndPathAndProperty(NodeType nodeType, bool exactType, string pathStart, bool orderByPath, List<QueryPropertyData> properties)
        {
            int[] typeIds = null;
            if (nodeType != null)
                typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();

            var ids = DataProvider.Current.QueryNodesByTypeAndPathAndProperty(typeIds, pathStart, orderByPath, properties);

            return new QueryResult(new NodeList<Node>(ids));
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByReference(string referenceName, int referredNodeId)
        {
            return QueryNodesByReferenceAndType(referenceName, referredNodeId, null, false);
        }
        /// <summary>
        /// DO NOT USE THIS METHOD DIRECTLY IN YOUR CODE.
        /// </summary>
        public static QueryResult QueryNodesByReferenceAndType(string referenceName, int referredNodeId, NodeType nodeType, bool exactType)
        {
            int[] typeIds = null;
            if (nodeType != null)
                typeIds = exactType ? new[] { nodeType.Id } : nodeType.GetAllTypes().ToIdArray();

            var ids = DataProvider.Current.QueryNodesByReferenceAndType(referenceName, referredNodeId, typeIds);

            return new QueryResult(new NodeList<Node>(ids));
        }
    }
}
