/**
 * @file Behavior.cs
 * @brief 行为图的数据模型：节点、连线与图，图的所有者是对象或地形实体，图与节点 ID 并入房间的统一 ID 空间。
 * BehaviorNodeKind(id, inputs, outputs, simulatable)：行为节点类型描述，登记在行为节点注册表中，声明端口与求解器能否模拟；openPassage 不再登记。
 * BehaviorNode(id, kind, cell, parameters)：图中的一个节点，cell 是节点记录的格坐标，房间扩建时必须随平移联动，没有格坐标时为空。
 * BehaviorEdge(fromNode, fromPort, toNode, toPort)：一条连线。
 * BehaviorGraph(id, ownerId, nodes, edges)：一张行为图，ownerId 指向拥有它的对象或地形实体 ID。
 */
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class BehaviorNodeKind
    {
        public string Id { get; }
        public IReadOnlyList<string> Inputs { get; }
        public IReadOnlyList<string> Outputs { get; }
        public bool Simulatable { get; }

        public BehaviorNodeKind(string id, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, bool simulatable)
        {
            Id = id;
            Inputs = inputs ?? new List<string>();
            Outputs = outputs ?? new List<string>();
            Simulatable = simulatable;
        }
    }

    public sealed class BehaviorNode
    {
        public string Id { get; }
        public string Kind { get; }
        public Cell? Cell { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }

        public BehaviorNode(string id, string kind, Cell? cell = null, IReadOnlyDictionary<string, string> parameters = null)
        {
            Id = id;
            Kind = kind;
            Cell = cell;
            Parameters = parameters ?? new Dictionary<string, string>();
        }
    }

    public sealed class BehaviorEdge
    {
        public string FromNode { get; }
        public string FromPort { get; }
        public string ToNode { get; }
        public string ToPort { get; }

        public BehaviorEdge(string fromNode, string fromPort, string toNode, string toPort)
        {
            FromNode = fromNode;
            FromPort = fromPort;
            ToNode = toNode;
            ToPort = toPort;
        }
    }

    public sealed class BehaviorGraph
    {
        public string Id { get; }
        public string OwnerId { get; }
        public IReadOnlyList<BehaviorNode> Nodes { get; }
        public IReadOnlyList<BehaviorEdge> Edges { get; }

        public BehaviorGraph(string id, string ownerId, IReadOnlyList<BehaviorNode> nodes, IReadOnlyList<BehaviorEdge> edges)
        {
            Id = id;
            OwnerId = ownerId;
            Nodes = nodes ?? new List<BehaviorNode>();
            Edges = edges ?? new List<BehaviorEdge>();
        }
    }
}
