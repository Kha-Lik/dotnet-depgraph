using DotNetDependencyGraph.Core.Domain.Communities;

namespace DotNetDependencyGraph.Core.Application.Scanning;

public sealed record ScanOptions
{
    public required string Root { get; init; }
    public string? OutputDirectory { get; init; }
    public IReadOnlyList<string> IncludePaths { get; init; } = [];
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
    public IReadOnlyList<string> TargetFrameworks { get; init; } = ["all"];
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];
    public IReadOnlyDictionary<string, string> GlobalProperties { get; init; } = new Dictionary<string, string>();
    public Action<string>? Progress { get; init; }
    public bool DetailedProgress { get; init; }
    public bool ComputeCommunities { get; init; } = true;
    public CommunitySettings? CommunitySettings { get; init; }
}
