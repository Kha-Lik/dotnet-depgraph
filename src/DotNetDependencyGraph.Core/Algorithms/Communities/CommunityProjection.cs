using System.Text.RegularExpressions;

using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Algorithms.Communities;

public sealed record CommunityProjectionVertex(string Id, IReadOnlyList<string> OriginalNodeIds);
public sealed record CommunityProjectionEdge(string Source, string Target, double Weight, bool Contracted = false, int? MinimumHiddenHops = null);

public sealed record CommunityProjection(
    IReadOnlyList<CommunityProjectionVertex> Vertices,
    IReadOnlyList<CommunityProjectionEdge> Edges,
    IReadOnlyDictionary<string, string> OriginalToVertex,
    IReadOnlyList<string> ExcludedTestNodeIds,
    IReadOnlyDictionary<string, CommunityNodeOwnership> Ownership,
    IReadOnlyDictionary<string, string> ProducedPackageToProject,
    CommunityProjectionMetadata Metadata,
    IReadOnlyList<GraphDiagnostic> Diagnostics);

public static class CommunityProjectionBuilder
{
    public const string CurrentPolicyVersion = "source-ownership-v1";

    public static CommunityProjection Build(DependencyGraph graph, CommunitySettings settings)
    {
        settings.Validate();
        var diagnostics = new List<GraphDiagnostic>();
        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var localProjects = graph.Nodes.Where(node => IsLocalProject(node, graph.Root)).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var producerGroups = graph.Edges
            .Where(edge => edge.Kind == EdgeKind.ProducesPackage && localProjects.Contains(edge.Source) && nodeById.GetValueOrDefault(edge.Target)?.Kind == NodeKind.Package)
            .GroupBy(edge => edge.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var producedPackageToProject = producerGroups.Where(item => item.Value.Length == 1)
            .ToDictionary(item => item.Key, item => item.Value[0], StringComparer.Ordinal);
        foreach (var item in producerGroups.Where(item => item.Value.Length > 1).OrderBy(item => item.Key, StringComparer.Ordinal))
            diagnostics.Add(new("community-producer-collapse-ambiguous", DiagnosticSeverity.Info,
                $"Package has {item.Value.Length} local producers and was not classified as a uniquely produced source package.", item.Key));

        var ownership = graph.Nodes.ToDictionary(node => node.Id,
            node => Classify(node, localProjects, producedPackageToProject, settings), StringComparer.Ordinal);
        var tests = settings.IncludeTestsInDetection
            ? []
            : graph.Nodes.Where(node => ownership[node.Id] == CommunityNodeOwnership.LocalProject && IsTest(node)).Select(node => node.Id).Order(StringComparer.Ordinal).ToArray();
        var eligible = graph.Nodes.Where(node => Included(ownership[node.Id], settings) && (settings.IncludeTestsInDetection || !IsTest(node)))
            .Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

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

        var collapsedProducerPairs = 0;
        foreach (var item in producedPackageToProject.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!eligible.Contains(item.Key) || !eligible.Contains(item.Value)) continue;
            Union(item.Value, item.Key); collapsedProducerPairs++;
        }

        var groups = eligible.GroupBy(Find, StringComparer.Ordinal)
            .Select(group => group.Order(StringComparer.Ordinal).ToArray())
            .OrderBy(group => group[0], StringComparer.Ordinal).ToArray();
        var vertices = groups.Select(group => new CommunityProjectionVertex("detection:" + group[0], group)).ToArray();
        var originalToVertex = vertices.SelectMany(vertex => vertex.OriginalNodeIds.Select(id => (id, vertex.Id)))
            .ToDictionary(item => item.id, item => item.Id, StringComparer.Ordinal);

        // Contexts are evidence, not multiplicity. For a fixed direction/kind we retain
        // the strongest logical relationship; explicitly opposite relationships add.
        var directed = new Dictionary<(string Source, string Target, EdgeKind Kind), (double Weight, bool Contracted, int? HiddenHops)>();
        foreach (var edge in graph.Edges.OrderBy(edge => edge.Source, StringComparer.Ordinal).ThenBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.Kind))
        {
            var weight = Weight(edge.Kind, settings.EdgeWeights);
            if (weight <= 0 || edge.Derived || !originalToVertex.TryGetValue(edge.Source, out var source) || !originalToVertex.TryGetValue(edge.Target, out var target) || source == target) continue;
            directed[(source, target, edge.Kind)] = (weight, false, null);
        }

        var directPairs = directed.Keys.Select(key => (key.Source, key.Target)).ToHashSet();
        foreach (var path in ContractedRelationships(graph, eligible, ownership))
        {
            var source = originalToVertex[path.Source]; var target = originalToVertex[path.Target];
            if (source == target || directPairs.Contains((source, target)) || settings.EdgeWeights.ContractedPath <= 0) continue;
            var key = (source, target, EdgeKind.ContractedPath);
            if (!directed.TryGetValue(key, out var prior) || path.HiddenHops < prior.HiddenHops)
                directed[key] = (settings.EdgeWeights.ContractedPath, true, path.HiddenHops);
        }

        var undirected = new Dictionary<(string Left, string Right), (double Weight, bool Contracted, int? HiddenHops)>();
        foreach (var item in directed)
        {
            var left = StringComparer.Ordinal.Compare(item.Key.Source, item.Key.Target) < 0 ? item.Key.Source : item.Key.Target;
            var right = left == item.Key.Source ? item.Key.Target : item.Key.Source;
            var old = undirected.GetValueOrDefault((left, right));
            undirected[(left, right)] = (old.Weight + item.Value.Weight, old.Contracted || item.Value.Contracted,
                Minimum(old.HiddenHops, item.Value.HiddenHops));
        }
        var edges = undirected.OrderBy(item => item.Key.Left, StringComparer.Ordinal).ThenBy(item => item.Key.Right, StringComparer.Ordinal)
            .Select(item => new CommunityProjectionEdge(item.Key.Left, item.Key.Right, item.Value.Weight, item.Value.Contracted, item.Value.HiddenHops)).ToArray();

        var scopeParts = new List<string> { "source-owned" };
        if (settings.IncludeUnmappedInternalPackages) scopeParts.Add("unmapped-internal");
        if (settings.IncludeThirdPartyPackages) scopeParts.Add("third-party");
        if (settings.IncludeSystemPackages) scopeParts.Add("system");
        if (settings.IncludeUnresolved) scopeParts.Add("unresolved");
        var metadata = new CommunityProjectionMetadata
        {
            PolicyVersion = CurrentPolicyVersion,
            Scope = string.Join("+", scopeParts),
            DetectionVertexCount = vertices.Length,
            DetectionNodeCount = eligible.Count,
            LocalProjectCount = ownership.Count(item => item.Value == CommunityNodeOwnership.LocalProject),
            LocalProducedPackageCount = ownership.Count(item => item.Value == CommunityNodeOwnership.LocalProducedPackage),
            IncludedUnmappedInternalPackageCount = eligible.Count(id => ownership[id] == CommunityNodeOwnership.UnmappedInternalPackage),
            IncludedSystemPackageCount = eligible.Count(id => ownership[id] == CommunityNodeOwnership.SystemPackage),
            IncludedThirdPartyPackageCount = eligible.Count(id => ownership[id] == CommunityNodeOwnership.ThirdPartyPackage),
            CollapsedProducerPairCount = collapsedProducerPairs,
            ExcludedTestProjectCount = tests.Length,
            ExcludedSystemPackageCount = ownership.Count(item => item.Value == CommunityNodeOwnership.SystemPackage && !eligible.Contains(item.Key)),
            ExcludedThirdPartyPackageCount = ownership.Count(item => item.Value == CommunityNodeOwnership.ThirdPartyPackage && !eligible.Contains(item.Key)),
            ExcludedUnresolvedExternalCount = ownership.Count(item => item.Value == CommunityNodeOwnership.UnresolvedExternal && !eligible.Contains(item.Key)),
            ContractedEdgeCount = edges.Count(edge => edge.Contracted)
        };
        diagnostics.Add(new("community-projection-scope", DiagnosticSeverity.Info,
            $"Community scope '{metadata.Scope}': {metadata.DetectionVertexCount} detection vertices from {metadata.DetectionNodeCount} included nodes; {metadata.CollapsedProducerPairCount} producer pairs collapsed; {metadata.ExcludedTestProjectCount} tests, {metadata.ExcludedSystemPackageCount} system packages, {metadata.ExcludedThirdPartyPackageCount} third-party packages, and {metadata.ExcludedUnresolvedExternalCount} unresolved external nodes excluded; {metadata.ContractedEdgeCount} contracted relationships used."));
        return new(vertices, edges, originalToVertex, tests, ownership, producedPackageToProject, metadata, diagnostics);
    }

    private static CommunityNodeOwnership Classify(GraphNode node, IReadOnlySet<string> localProjects,
        IReadOnlyDictionary<string, string> producedPackageToProject, CommunitySettings settings)
    {
        if (localProjects.Contains(node.Id)) return CommunityNodeOwnership.LocalProject;
        if (producedPackageToProject.ContainsKey(node.Id)) return CommunityNodeOwnership.LocalProducedPackage;
        if (node.Kind is NodeKind.UnresolvedLibrary or NodeKind.UnresolvedProject or NodeKind.Project) return CommunityNodeOwnership.UnresolvedExternal;
        var packageIdentity = node.Id.StartsWith("package:", StringComparison.OrdinalIgnoreCase) ? node.Id["package:".Length..] : node.Id;
        if (settings.InternalPackagePatterns.Any(pattern => Glob(node.Label, pattern) || Glob(packageIdentity, pattern)))
            return CommunityNodeOwnership.UnmappedInternalPackage;
        return IsSystemPackage(node.Label) ? CommunityNodeOwnership.SystemPackage : CommunityNodeOwnership.ThirdPartyPackage;
    }

    private static bool Included(CommunityNodeOwnership ownership, CommunitySettings settings) => ownership switch
    {
        CommunityNodeOwnership.LocalProject or CommunityNodeOwnership.LocalProducedPackage => true,
        CommunityNodeOwnership.UnmappedInternalPackage => settings.IncludeUnmappedInternalPackages,
        CommunityNodeOwnership.SystemPackage => settings.IncludeSystemPackages,
        CommunityNodeOwnership.ThirdPartyPackage => settings.IncludeThirdPartyPackages,
        CommunityNodeOwnership.UnresolvedExternal => settings.IncludeUnresolved,
        _ => false
    };

    private static IEnumerable<(string Source, string Target, int HiddenHops)> ContractedRelationships(
        DependencyGraph graph, IReadOnlySet<string> eligible, IReadOnlyDictionary<string, CommunityNodeOwnership> ownership)
    {
        var dependencies = graph.Edges.Where(edge => IsRawDependency(edge) && !edge.Derived)
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.Kind).ToArray(), StringComparer.Ordinal);
        bool Hidden(string id) => !eligible.Contains(id) && ownership[id] is not CommunityNodeOwnership.LocalProject and not CommunityNodeOwnership.LocalProducedPackage;
        var relationships = new Dictionary<(string Source, string Target), int>();
        foreach (var source in eligible.Order(StringComparer.Ordinal))
        {
            if (!dependencies.TryGetValue(source, out var starts)) continue;
            var queue = new Queue<(string Node, PathContext Context, int HiddenHops)>();
            var seen = new HashSet<(string Node, string Owner, string Tfm, string Rid)>(new PathStateComparer());
            foreach (var edge in starts.Where(edge => Hidden(edge.Target)))
                foreach (var context in Contexts(edge)) queue.Enqueue((edge.Target, context, 1));
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                if (!seen.Add((state.Node, state.Context.Owner, state.Context.Tfm, state.Context.Rid ?? ""))) continue;
                if (!dependencies.TryGetValue(state.Node, out var nextEdges)) continue;
                foreach (var edge in nextEdges)
                {
                    foreach (var nextContext in Contexts(edge))
                    {
                        if (!TryMerge(state.Context, nextContext, out var merged)) continue;
                        if (eligible.Contains(edge.Target))
                        {
                            if (edge.Target == source) continue;
                            var key = (source, edge.Target);
                            relationships[key] = Math.Min(relationships.GetValueOrDefault(key, int.MaxValue), state.HiddenHops);
                        }
                        else if (Hidden(edge.Target)) queue.Enqueue((edge.Target, merged, state.HiddenHops + 1));
                    }
                }
            }
        }
        return relationships.OrderBy(item => item.Key.Source, StringComparer.Ordinal).ThenBy(item => item.Key.Target, StringComparer.Ordinal)
            .Select(item => (item.Key.Source, item.Key.Target, item.Value));
    }

    private static IEnumerable<PathContext> Contexts(GraphEdge edge)
        => edge.Contexts.Count == 0 ? [new("", "", null)] : edge.Contexts.Select(context => new PathContext(context.OwnerProjectId ?? "", context.TargetFramework ?? "", context.RuntimeIdentifier));

    private static bool TryMerge(PathContext left, PathContext right, out PathContext merged)
    {
        static bool Compatible(string left, string right) => left.Length == 0 || right.Length == 0 || left.Equals(right, StringComparison.OrdinalIgnoreCase);
        static string Specific(string left, string right) => left.Length == 0 ? right : left;
        var leftRid = left.Rid ?? ""; var rightRid = right.Rid ?? "";
        if (!Compatible(left.Owner, right.Owner) || !Compatible(left.Tfm, right.Tfm) || !Compatible(leftRid, rightRid)) { merged = default; return false; }
        var rid = Specific(leftRid, rightRid);
        merged = new(Specific(left.Owner, right.Owner), Specific(left.Tfm, right.Tfm), rid.Length > 0 ? rid : null);
        return true;
    }

    private static bool IsLocalProject(GraphNode node, string root)
    {
        if (node.Kind != NodeKind.Project || string.IsNullOrWhiteSpace(node.Path)) return false;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(node.Path) ? node.Path : Path.Combine(fullRoot, node.Path));
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool IsSystemPackage(string name)
    {
        var prefixes = new[] { "System.", "runtime.", "Microsoft.AspNetCore.", "Microsoft.Extensions.", "Microsoft.NETCore.", "Microsoft.NET.", "Microsoft.Win32.", "Microsoft.CSharp", "Microsoft.VisualBasic" };
        return name.Equals("NETStandard.Library", StringComparison.OrdinalIgnoreCase)
            || prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Glob(string value, string pattern)
        => Regex.IsMatch(value, "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsTest(GraphNode node)
        => string.Equals(node.Classification, "test-project", StringComparison.OrdinalIgnoreCase);

    private static bool IsRawDependency(GraphEdge edge)
        => edge.Kind is EdgeKind.ProjectReference or EdgeKind.PackageReference or EdgeKind.PackageDependency;

    private static double Weight(EdgeKind kind, CommunityEdgeWeights weights) => kind switch
    {
        EdgeKind.ProjectReference => weights.ProjectReference,
        EdgeKind.PackageReference => weights.PackageReference,
        EdgeKind.PackageDependency => weights.PackageDependency,
        EdgeKind.ContractedPath => weights.ContractedPath,
        _ => 0
    };

    private static int? Minimum(int? left, int? right) => left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);
    private readonly record struct PathContext(string Owner, string Tfm, string? Rid);

    private sealed class PathStateComparer : IEqualityComparer<(string Node, string Owner, string Tfm, string Rid)>
    {
        public bool Equals((string Node, string Owner, string Tfm, string Rid) left, (string Node, string Owner, string Tfm, string Rid) right)
            => StringComparer.Ordinal.Equals(left.Node, right.Node) && StringComparer.Ordinal.Equals(left.Owner, right.Owner)
                && StringComparer.OrdinalIgnoreCase.Equals(left.Tfm, right.Tfm) && StringComparer.OrdinalIgnoreCase.Equals(left.Rid, right.Rid);
        public int GetHashCode((string Node, string Owner, string Tfm, string Rid) value)
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Node), StringComparer.Ordinal.GetHashCode(value.Owner), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Tfm), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Rid));
    }
}
