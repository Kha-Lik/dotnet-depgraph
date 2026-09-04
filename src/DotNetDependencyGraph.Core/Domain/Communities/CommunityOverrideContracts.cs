using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Domain.Communities;

public sealed record CommunityStyleOverride
{
    public required string DetectedAnchorNodeId { get; init; }
    public string? CommunityKey { get; init; }
    public string? Name { get; init; }
    public string? Color { get; init; }
}

public sealed record ManualCommunity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
}

public sealed record CommunityOverrideDocument
{
    public int Version { get; init; } = 1;
    public required string GraphFingerprint { get; init; }
    public IReadOnlyList<CommunityStyleOverride> CommunityOverrides { get; init; } = [];
    public IReadOnlyList<ManualCommunity> ManualCommunities { get; init; } = [];
    public IReadOnlyDictionary<string, string> NodeAssignments { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<GraphDiagnostic> OverrideDiagnostics { get; init; } = [];
}
