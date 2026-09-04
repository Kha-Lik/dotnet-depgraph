namespace DotNetDependencyGraph.Core;

public enum FilterMode { Strict, Contract }

public static class GraphFilter
{
    public static DependencyGraph Apply(
        DependencyGraph raw,
        IReadOnlyList<string> includes,
        IReadOnlyList<string> excludes,
        FilterMode mode,
        IReadOnlyList<string>? includeProjects = null,
        IReadOnlyList<string>? excludeProjects = null,
        int sampleLimit = 3,
        int pathLimit = 1000)
    {
        includeProjects ??= [];
        excludeProjects ??= [];

        bool Retain(GraphNode node)
        {
            if (node.Kind == NodeKind.Package)
            {
                var packageId = node.Id.StartsWith("package:", StringComparison.Ordinal) ? node.Id["package:".Length..] : node.Label;
                return (includes.Count == 0 || includes.Any(x => Glob.IsMatch(packageId, x))) && !excludes.Any(x => Glob.IsMatch(packageId, x));
            }

            if (node.Kind != NodeKind.Project) return true;
            var path = ProjectDiscovery.Normalize(node.Path ?? node.Id["project:".Length..]);
            return (includeProjects.Count == 0 || includeProjects.Any(x => Glob.IsMatch(path, x)))
                && !excludeProjects.Any(x => Glob.IsMatch(path, x));
        }
        var retained = raw.Nodes.Where(Retain).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var nodes = raw.Nodes.Where(x => retained.Contains(x.Id)).ToArray();
        var direct = raw.Edges.Where(x => retained.Contains(x.Source) && retained.Contains(x.Target)).ToList();
        if (mode == FilterMode.Strict) return WithFilterIsolationDiagnostics(raw, GraphAnalysis.Analyze(raw with { Nodes = nodes, Edges = direct.ToArray() }, computeCommunities: false), mode);

        var traversable = raw.Edges.Where(GraphAnalysis.IsDependency).GroupBy(x => x.Source)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var contracted = new Dictionary<(string, string), ContractState>();
        foreach (var source in retained.Order(StringComparer.Ordinal))
        {
            if (!traversable.TryGetValue(source, out var starts)) continue;
            foreach (var edge in starts.Where(x => !retained.Contains(x.Target)))
            {
                var stack = new Stack<PathState>(); stack.Push(new(edge.Target, [edge.Target], [edge], new HashSet<string>([source, edge.Target], StringComparer.Ordinal)));
                while (stack.Count > 0)
                {
                    var state = stack.Pop();
                    if (!traversable.TryGetValue(state.Node, out var nextEdges)) continue;
                    foreach (var next in nextEdges)
                    {
                        if (next.Target == source) continue;
                        if (retained.Contains(next.Target))
                        {
                            var key = (source, next.Target);
                            if (!contracted.TryGetValue(key, out var c)) contracted[key] = c = new();
                            c.Add(state.Hidden, state.Edges.Append(next), sampleLimit, pathLimit);
                        }
                        else if (!state.Seen.Contains(next.Target))
                        {
                            var seen = new HashSet<string>(state.Seen, StringComparer.Ordinal) { next.Target };
                            stack.Push(new(next.Target, state.Hidden.Append(next.Target).ToArray(), state.Edges.Append(next).ToArray(), seen));
                        }
                    }
                }
            }
        }
        direct.AddRange(contracted.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2).Select(x => x.Value.Build(x.Key.Item1, x.Key.Item2)));
        return WithFilterIsolationDiagnostics(raw, GraphAnalysis.Analyze(raw with { Nodes = nodes, Edges = direct.OrderBy(x => x.Source).ThenBy(x => x.Target).ThenBy(x => x.Kind).ToArray() }, computeCommunities: false), mode);
    }

    private static DependencyGraph WithFilterIsolationDiagnostics(DependencyGraph raw, DependencyGraph display, FilterMode mode)
    {
        var rawConnected = raw.Edges.Where(GraphAnalysis.IsDependency).SelectMany(x => new[] { x.Source, x.Target }).ToHashSet(StringComparer.Ordinal);
        var added = display.Nodes.Where(x => x.InDegree == 0 && x.OutDegree == 0 && rawConnected.Contains(x.Id)).Select(x => new GraphDiagnostic(
            mode == FilterMode.Strict ? "isolated-by-strict-filter" : "isolated-after-contract-filter", DiagnosticSeverity.Info,
            mode == FilterMode.Strict ? "Filtering removed this node's connecting paths in strict mode." : "No retained dependency endpoint was reachable through the hidden dependency paths.", x.Id, x.Path));
        return display with { Diagnostics = display.Diagnostics.Concat(added).OrderBy(x => x.Code).ThenBy(x => x.ProjectId).ThenBy(x => x.Message).ToArray() };
    }

    private sealed record PathState(string Node, IReadOnlyList<string> Hidden, IEnumerable<GraphEdge> Edges, HashSet<string> Seen);
    private sealed class ContractState
    {
        private int _count; private bool _limited; private int _minimum = int.MaxValue;
        private readonly List<IReadOnlyList<string>> _samples = []; private readonly List<EdgeContext> _contexts = [];
        public void Add(IReadOnlyList<string> hidden, IEnumerable<GraphEdge> edges, int sampleLimit, int pathLimit)
        {
            if (_count < pathLimit) _count++; else _limited = true;
            _minimum = Math.Min(_minimum, hidden.Count);
            if (_samples.Count < sampleLimit) _samples.Add(hidden.ToArray());
            _contexts.AddRange(edges.SelectMany(x => x.Contexts));
        }
        public GraphEdge Build(string source, string target) => new()
        {
            Id = $"edge:{EdgeKind.ContractedPath}:{source}->{target}",
            Source = source,
            Target = target,
            Kind = EdgeKind.ContractedPath,
            Derived = true,
            MinimumHiddenHops = _minimum,
            PathCount = _count,
            PathCountLimited = _limited,
            HiddenPathSamples = _samples,
            Contexts = _contexts.Distinct().OrderBy(x => x.OwnerProjectId).ThenBy(x => x.TargetFramework).Take(100).ToArray()
        };
    }
}
