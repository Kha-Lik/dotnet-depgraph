namespace DotNetDependencyGraph.Core;

public static class GraphAnalysis
{
    public static DependencyGraph Analyze(DependencyGraph graph, int seed = 42)
    {
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
        var communities = LabelCommunities(ids, outgoing, incoming, seed);
        var nodes = graph.Nodes.Select(node =>
        {
            var down = Reach(node.Id, outgoing); down.Remove(node.Id);
            var up = Reach(node.Id, incoming); up.Remove(node.Id);
            var degree = outgoing[node.Id].Count + incoming[node.Id].Count;
            return node with
            {
                Component = componentMap[node.Id],
                Community = communities[node.Id],
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
        return graph with { Nodes = nodes };
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

    private static Dictionary<string, int> LabelCommunities(HashSet<string> ids, Dictionary<string, HashSet<string>> outgoing, Dictionary<string, HashSet<string>> incoming, int seed)
    {
        var labels = ids.Order(StringComparer.Ordinal).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var order = ids.OrderBy(x => StableHash(x, seed)).ThenBy(x => x, StringComparer.Ordinal).ToArray();
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var changed = false;
            foreach (var id in order)
            {
                var neighbors = outgoing[id].Concat(incoming[id]).Distinct().ToArray(); if (neighbors.Length == 0) continue;
                var best = neighbors.GroupBy(x => labels[x]).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
                if (best != labels[id]) { labels[id] = best; changed = true; }
            }
            if (!changed) break;
        }
        var normalized = labels.Values.Distinct().Order().Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i);
        return labels.ToDictionary(x => x.Key, x => normalized[x.Value], StringComparer.Ordinal);
    }

    private static int StableHash(string value, int seed)
    { unchecked { var h = (uint)seed; foreach (var c in value) h = (h ^ c) * 16777619; return (int)h; } }
}
