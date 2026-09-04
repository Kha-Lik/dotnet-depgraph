using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Application.Analysis;

public static class GraphAnalysis
{
    public static DependencyGraph Analyze(DependencyGraph graph, int seed = 42, CommunitySettings? communitySettings = null, bool computeCommunities = true, Action<string>? progress = null)
    {
        if (computeCommunities) progress?.Invoke($"Computing graph metrics for {graph.Nodes.Count} node(s) and {graph.Edges.Count} edge(s).");
        var dependencyEdges = graph.Edges.Where(IsDependency).ToArray();
        var ids = graph.Nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var outgoing = ids.ToDictionary(x => x, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var incoming = ids.ToDictionary(x => x, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in dependencyEdges.Where(x => ids.Contains(x.Source) && ids.Contains(x.Target)))
        { outgoing[edge.Source].Add(edge.Target); incoming[edge.Target].Add(edge.Source); }
        var componentOutgoing = ids.ToDictionary(x => x, x => new HashSet<string>(outgoing[x], StringComparer.Ordinal), StringComparer.Ordinal);
        var componentIncoming = ids.ToDictionary(x => x, x => new HashSet<string>(incoming[x], StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(x => ids.Contains(x.Source) && ids.Contains(x.Target)))
        { componentOutgoing[edge.Source].Add(edge.Target); componentIncoming[edge.Target].Add(edge.Source); }
        var components = WeakComponents(ids, componentOutgoing, componentIncoming);
        var componentMap = components.SelectMany((c, i) => c.Select(n => (n, i))).ToDictionary(x => x.n, x => x.i, StringComparer.Ordinal);
        var cycleNodes = CyclicNodes(ids, outgoing);
        var nodes = graph.Nodes.Select(node =>
        {
            var down = Reach(node.Id, outgoing); down.Remove(node.Id);
            var up = Reach(node.Id, incoming); up.Remove(node.Id);
            var degree = outgoing[node.Id].Count + incoming[node.Id].Count;
            return node with
            {
                Component = componentMap[node.Id],
                InDegree = incoming[node.Id].Count,
                OutDegree = outgoing[node.Id].Count,
                DirectDependencies = outgoing[node.Id].Count,
                TransitiveDependencies = down.Count,
                DirectDependents = incoming[node.Id].Count,
                TransitiveDependents = up.Count,
                Centrality = Math.Round(Math.Log2(1 + degree + up.Count * .25), 4),
                InCycle = cycleNodes.Contains(node.Id)
            };
        }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var analyzed = graph with { SchemaVersion = "2.0", Nodes = nodes };
        if (!computeCommunities) return analyzed;
        progress?.Invoke($"Graph metrics complete: {components.Count} weak component(s), {cycleNodes.Count} node(s) in dependency cycles.");
        var withCommunities = CommunityAnalyzer.Analyze(analyzed, communitySettings ?? new CommunitySettings { Seed = seed }, progress);
        var standard = withCommunities.CommunityAnalysis!.GranularityAssignments["standard"];
        var integerIds = standard.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);
        return withCommunities with { Nodes = withCommunities.Nodes.Select(node => node with { Community = integerIds[standard[node.Id]] }).ToArray() };
    }

    public static bool IsDependency(GraphEdge edge) => edge.Kind is EdgeKind.ProjectReference or EdgeKind.PackageReference or EdgeKind.PackageDependency or EdgeKind.ContractedPath;

    private static HashSet<string> Reach(string start, IReadOnlyDictionary<string, HashSet<string>> map)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); var stack = new Stack<string>(); stack.Push(start);
        while (stack.Count > 0) { var n = stack.Pop(); if (!seen.Add(n)) continue; foreach (var next in map[n]) stack.Push(next); }
        return seen;
    }

    private static List<HashSet<string>> WeakComponents(HashSet<string> ids, Dictionary<string, HashSet<string>> outgoing, Dictionary<string, HashSet<string>> incoming)
    {
        var remaining = new HashSet<string>(ids, StringComparer.Ordinal); var result = new List<HashSet<string>>();
        while (remaining.Count > 0)
        {
            var start = remaining.Order(StringComparer.Ordinal).First(); var component = new HashSet<string>(StringComparer.Ordinal); var stack = new Stack<string>(); stack.Push(start);
            while (stack.Count > 0) { var n = stack.Pop(); if (!component.Add(n)) continue; remaining.Remove(n); foreach (var x in outgoing[n]) stack.Push(x); foreach (var x in incoming[n]) stack.Push(x); }
            result.Add(component);
        }
        return result.OrderByDescending(x => x.Count).ThenBy(x => x.Min(StringComparer.Ordinal), StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> CyclicNodes(HashSet<string> ids, Dictionary<string, HashSet<string>> outgoing)
    {
        var index = 0; var indices = new Dictionary<string, int>(); var low = new Dictionary<string, int>(); var stack = new Stack<string>(); var onStack = new HashSet<string>(); var cyclic = new HashSet<string>();
        void Visit(string v)
        {
            indices[v] = low[v] = index++; stack.Push(v); onStack.Add(v);
            foreach (var w in outgoing[v]) { if (!indices.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); } else if (onStack.Contains(w)) low[v] = Math.Min(low[v], indices[w]); }
            if (low[v] != indices[v]) return;
            var component = new List<string>(); string w2; do { w2 = stack.Pop(); onStack.Remove(w2); component.Add(w2); } while (w2 != v);
            if (component.Count > 1 || outgoing[v].Contains(v)) foreach (var n in component) cyclic.Add(n);
        }
        foreach (var id in ids.Order(StringComparer.Ordinal)) if (!indices.ContainsKey(id)) Visit(id);
        return cyclic;
    }

}
