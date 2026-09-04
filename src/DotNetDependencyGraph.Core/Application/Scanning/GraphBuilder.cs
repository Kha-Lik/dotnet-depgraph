using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Application.Scanning;

public sealed class GraphBuilder(string root)
{
    private readonly Dictionary<string, NodeState> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string, EdgeKind), EdgeState> _edges = new();
    private readonly List<GraphDiagnostic> _diagnostics = [];

    public void AddNode(GraphNode node)
    {
        if (!_nodes.TryGetValue(node.Id, out var state)) _nodes[node.Id] = new(node);
        else state.Merge(node);
    }

    public void AddEdge(string source, string target, EdgeKind kind, EdgeContext context)
    {
        var key = (source, target, kind);
        if (!_edges.TryGetValue(key, out var state)) _edges[key] = state = new(source, target, kind);
        state.Contexts.Add(context);
    }

    public void Diagnostic(GraphDiagnostic diagnostic) => _diagnostics.Add(diagnostic);
    public int DiagnosticCount(string code) => _diagnostics.Count(x => x.Code == code);
    public bool HasErrors => _diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error);

    public DependencyGraph Build(GraphCompleteness completeness, Action<string>? progress = null,
        bool computeCommunities = true, CommunitySettings? communitySettings = null)
    {
        var edges = _edges.Values.Select(x => x.Build()).OrderBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Target, StringComparer.Ordinal).ThenBy(x => x.Kind).ToArray();
        var nodes = _nodes.Values.Select(x => x.Build()).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var graph = new DependencyGraph
        {
            Root = root,
            Nodes = nodes,
            Edges = edges,
            Diagnostics = _diagnostics.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.ProjectId, StringComparer.Ordinal).ThenBy(x => x.Message, StringComparer.Ordinal).ToArray(),
            Completeness = completeness,
            TargetFrameworks = edges.SelectMany(x => x.Contexts).Select(x => x.TargetFramework).Where(x => x.Length > 0).Distinct().Order().ToArray(),
            RuntimeIdentifiers = edges.SelectMany(x => x.Contexts).Select(x => x.RuntimeIdentifier).Where(x => x is not null).Cast<string>().Distinct().Order().ToArray()
        };
        return GraphAnalysis.Analyze(graph, communitySettings: communitySettings, computeCommunities: computeCommunities, progress: progress);
    }

    private sealed class NodeState(GraphNode node)
    {
        private GraphNode _node = node;
        private readonly HashSet<string> _versions = new(node.Versions, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tfms = new(node.TargetFrameworks, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _rids = new(node.RuntimeIdentifiers, StringComparer.OrdinalIgnoreCase);
        public void Merge(GraphNode other)
        {
            foreach (var x in other.Versions) _versions.Add(x);
            foreach (var x in other.TargetFrameworks) _tfms.Add(x);
            foreach (var x in other.RuntimeIdentifiers) _rids.Add(x);
            if (_node.Path is null && other.Path is not null) _node = other;
        }
        public GraphNode Build() => _node with
        {
            Versions = _versions.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            VersionSkew = _versions.Count > 1,
            TargetFrameworks = _tfms.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            RuntimeIdentifiers = _rids.Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private sealed class EdgeState(string source, string target, EdgeKind kind)
    {
        public List<EdgeContext> Contexts { get; } = [];
        public GraphEdge Build()
        {
            var contexts = Contexts.GroupBy(x => new { x.OwnerProjectId, x.AssetsFile, x.TargetFramework, x.RuntimeIdentifier, x.RequestedVersion, x.ResolvedVersion, x.Direct })
                .Select(g => g.First() with { ObservationCount = g.Sum(x => x.ObservationCount) })
                .OrderBy(x => x.OwnerProjectId).ThenBy(x => x.TargetFramework).ThenBy(x => x.RuntimeIdentifier).ToArray();
            return new() { Id = $"edge:{kind}:{source}->{target}", Source = source, Target = target, Kind = kind, Contexts = contexts };
        }
    }
}
