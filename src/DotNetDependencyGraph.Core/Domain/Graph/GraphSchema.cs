namespace DotNetDependencyGraph.Core.Domain.Graph;

public static class GraphSchema
{
    public static void Validate(DependencyGraph graph)
    {
        if (!string.Equals(graph.SchemaVersion, "2.0", StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported graph schemaVersion '{graph.SchemaVersion}'. Expected '2.0'.");
        var analysis = graph.CommunityAnalysis ?? throw new ArgumentException("Graph schema 2.0 requires a non-null communityAnalysis section.");
        if (!string.Equals(analysis.Version, "1.0", StringComparison.Ordinal) || !string.Equals(analysis.Algorithm, "leiden-cpm", StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported communityAnalysis version/algorithm '{analysis.Version}/{analysis.Algorithm}'.");
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != graph.Nodes.Count) throw new ArgumentException("Graph contains duplicate node IDs.");
        var duplicateEdge = graph.Edges.GroupBy(edge => edge.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateEdge is not null) throw new ArgumentException($"Graph contains duplicate edge ID '{duplicateEdge.Key}'.");
        var dangling = graph.Edges.FirstOrDefault(edge => !ids.Contains(edge.Source) || !ids.Contains(edge.Target));
        if (dangling is not null) throw new ArgumentException($"Edge '{dangling.Id}' has a missing endpoint.");
        foreach (var node in graph.Nodes)
        {
            if (!analysis.NodeAssignments.TryGetValue(node.Id, out var assignment) || assignment.DetectedCommunityPath.Count == 0)
                throw new ArgumentException($"communityAnalysis has no detected assignment for node '{node.Id}'.");
            foreach (var granularity in new[] { "coarse", "standard", "fine" })
                if (!analysis.GranularityAssignments.TryGetValue(granularity, out var map) || !map.ContainsKey(node.Id))
                    throw new ArgumentException($"communityAnalysis granularity '{granularity}' has no assignment for node '{node.Id}'.");
        }
        if (analysis.ResolutionProfile.Any(candidate => !candidate.Connected))
            throw new ArgumentException("communityAnalysis contains a disconnected Leiden partition.");
        var fingerprint = GraphFingerprint.Calculate(graph);
        if (!string.Equals(fingerprint, analysis.GraphFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("communityAnalysis graphFingerprint does not match canonical nodes and edges.");
    }
}
