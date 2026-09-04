using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Algorithms.Communities;

public sealed record CommunityProjectionVertex(string Id, IReadOnlyList<string> OriginalNodeIds);
public sealed record CommunityProjectionEdge(string Source, string Target, double Weight);

public sealed record CommunityProjection(
    IReadOnlyList<CommunityProjectionVertex> Vertices,
    IReadOnlyList<CommunityProjectionEdge> Edges,
    IReadOnlyDictionary<string, string> OriginalToVertex,
    IReadOnlyList<string> ExcludedTestNodeIds,
    IReadOnlyList<GraphDiagnostic> Diagnostics);

public static class CommunityProjectionBuilder
{
    public static CommunityProjection Build(DependencyGraph graph, CommunitySettings settings)
    {
        settings.Validate();
        var diagnostics = new List<GraphDiagnostic>();
        var eligible = graph.Nodes.Where(node => Eligible(node, settings)).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var tests = settings.IncludeTestsInDetection ? [] : graph.Nodes.Where(IsTest).Select(node => node.Id).Order(StringComparer.Ordinal).ToArray();
        var parent = eligible.ToDictionary(id => id, id => id, StringComparer.Ordinal);

        string Find(string id)
        {
            while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; }
            return id;
        }
        void Union(string left, string right)
        {
            left = Find(left); right = Find(right); if (left == right) return;
            var first = StringComparer.Ordinal.Compare(left, right) <= 0 ? left : right;
            var second = first == left ? right : left; parent[second] = first;
        }

        var producerEdges = graph.Edges.Where(edge => edge.Kind == EdgeKind.ProducesPackage && eligible.Contains(edge.Source) && eligible.Contains(edge.Target)).ToArray();
        foreach (var packageGroup in producerEdges.GroupBy(edge => edge.Target, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var producers = packageGroup.Select(edge => edge.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (producers.Length == 1) Union(producers[0], packageGroup.Key);
            else diagnostics.Add(new("community-producer-collapse-ambiguous", DiagnosticSeverity.Info,
                $"Package has {producers.Length} eligible producers and was not collapsed for community detection.", packageGroup.Key));
        }

        var groups = eligible.GroupBy(Find, StringComparer.Ordinal)
            .Select(group => group.Order(StringComparer.Ordinal).ToArray())
            .OrderBy(group => group[0], StringComparer.Ordinal).ToArray();
        var vertices = groups.Select(group => new CommunityProjectionVertex("detection:" + group[0], group)).ToArray();
        var originalToVertex = vertices.SelectMany(vertex => vertex.OriginalNodeIds.Select(id => (id, vertex.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);

        // Contexts are evidence, not multiplicity. For a fixed direction/kind we retain
        // the strongest collapsed logical relationship; explicitly opposite relationships add.
        var directed = new Dictionary<(string Source, string Target, EdgeKind Kind), double>();
        foreach (var edge in graph.Edges.OrderBy(edge => edge.Source, StringComparer.Ordinal).ThenBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.Kind))
        {
            var weight = Weight(edge.Kind, settings.EdgeWeights);
            if (weight <= 0 || !originalToVertex.TryGetValue(edge.Source, out var source) || !originalToVertex.TryGetValue(edge.Target, out var target) || source == target) continue;
            var key = (source, target, edge.Kind);
            if (!directed.TryGetValue(key, out var old) || weight > old) directed[key] = weight;
        }
        var undirected = new Dictionary<(string Left, string Right), double>();
        foreach (var item in directed)
        {
            var left = StringComparer.Ordinal.Compare(item.Key.Source, item.Key.Target) < 0 ? item.Key.Source : item.Key.Target;
            var right = left == item.Key.Source ? item.Key.Target : item.Key.Source;
            undirected[(left, right)] = undirected.GetValueOrDefault((left, right)) + item.Value;
        }
        var edges = undirected.OrderBy(item => item.Key.Left, StringComparer.Ordinal).ThenBy(item => item.Key.Right, StringComparer.Ordinal)
            .Select(item => new CommunityProjectionEdge(item.Key.Left, item.Key.Right, item.Value)).ToArray();
        return new(vertices, edges, originalToVertex, tests, diagnostics);
    }

    private static bool Eligible(GraphNode node, CommunitySettings settings)
        => (settings.IncludeTestsInDetection || !IsTest(node))
            && (settings.IncludeUnresolved || node.Kind is not NodeKind.UnresolvedLibrary and not NodeKind.UnresolvedProject);

    internal static bool IsTest(GraphNode node)
        => string.Equals(node.Classification, "test-project", StringComparison.OrdinalIgnoreCase);

    private static double Weight(EdgeKind kind, CommunityEdgeWeights weights) => kind switch
    {
        EdgeKind.ProjectReference => weights.ProjectReference,
        EdgeKind.PackageReference => weights.PackageReference,
        EdgeKind.PackageDependency => weights.PackageDependency,
        _ => 0
    };
}
