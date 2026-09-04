using DotNetDependencyGraph.Core.Domain.Communities;

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
            if (!analysis.NodeAssignments.TryGetValue(node.Id, out var assignment))
                throw new ArgumentException($"communityAnalysis has no detected assignment for node '{node.Id}'.");
            foreach (var granularity in new[] { "coarse", "standard", "fine" })
            {
                if (!analysis.GranularityAssignments.TryGetValue(granularity, out var map))
                    throw new ArgumentException($"communityAnalysis has no '{granularity}' granularity map.");
                if (assignment.DetectedCommunityPath.Count > 0 && !map.ContainsKey(node.Id))
                    throw new ArgumentException($"communityAnalysis granularity '{granularity}' has no assignment for included node '{node.Id}'.");
            }
        }
        if (analysis.Projection.PolicyVersion.Length > 0)
        {
            if (analysis.NodeOwnership.Count != graph.Nodes.Count || graph.Nodes.Any(node => !analysis.NodeOwnership.ContainsKey(node.Id)))
                throw new ArgumentException("communityAnalysis nodeOwnership must classify every canonical node.");
            var detectionNodeCount = analysis.NodeOwnership.Count(item => item.Value switch
            {
                CommunityNodeOwnership.LocalProject => analysis.Settings.IncludeTestsInDetection || !string.Equals(graph.Nodes.First(node => node.Id == item.Key).Classification, "test-project", StringComparison.OrdinalIgnoreCase),
                CommunityNodeOwnership.LocalProducedPackage => true,
                CommunityNodeOwnership.UnmappedInternalPackage => analysis.Settings.IncludeUnmappedInternalPackages,
                CommunityNodeOwnership.SystemPackage => analysis.Settings.IncludeSystemPackages,
                CommunityNodeOwnership.ThirdPartyPackage => analysis.Settings.IncludeThirdPartyPackages,
                CommunityNodeOwnership.UnresolvedExternal => analysis.Settings.IncludeUnresolved,
                _ => false
            });
            if (detectionNodeCount != analysis.Projection.DetectionNodeCount
                || detectionNodeCount - analysis.Projection.CollapsedProducerPairCount != analysis.Projection.DetectionVertexCount)
                throw new ArgumentException("communityAnalysis projection node/vertex counts are inconsistent.");
            var selected = analysis.ResolutionProfile.Single(candidate => candidate.SelectedStandard);
            if (selected.Sizes.Sum() != analysis.Projection.DetectionVertexCount)
                throw new ArgumentException("communityAnalysis selected resolution sizes do not match detection vertices.");
            var standard = analysis.GranularityAssignments["standard"];
            if (!analysis.Settings.IncludeSystemPackages && standard.Keys.Any(id => analysis.NodeOwnership[id] == CommunityNodeOwnership.SystemPackage)
                || !analysis.Settings.IncludeThirdPartyPackages && standard.Keys.Any(id => analysis.NodeOwnership[id] == CommunityNodeOwnership.ThirdPartyPackage))
                throw new ArgumentException("communityAnalysis default scope assigns an excluded external package.");
            var invalidRepresentative = analysis.Communities.SelectMany(community => community.RepresentativeNodeIds).FirstOrDefault(id =>
                !analysis.NodeOwnership.TryGetValue(id, out var ownership) || ownership is not CommunityNodeOwnership.LocalProject and not CommunityNodeOwnership.LocalProducedPackage);
            if (invalidRepresentative is not null) throw new ArgumentException($"Community representative '{invalidRepresentative}' is not source-owned.");
            var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            if (analysis.Communities.Any(community => community.RepresentativeNodeIds.Select(id => nodesById[id].Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != community.RepresentativeNodeIds.Count))
                throw new ArgumentException("communityAnalysis contains duplicate representative labels.");
        }
        if (analysis.ResolutionProfile.Any(candidate => !candidate.Connected))
            throw new ArgumentException("communityAnalysis contains a disconnected Leiden partition.");
        var fingerprint = GraphFingerprint.Calculate(graph);
        if (!string.Equals(fingerprint, analysis.GraphFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("communityAnalysis graphFingerprint does not match canonical nodes and edges.");
    }
}
