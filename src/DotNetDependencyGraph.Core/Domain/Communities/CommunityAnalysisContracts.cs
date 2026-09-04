using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Domain.Communities;

public sealed record CommunityEdgeWeights
{
    public double ProjectReference { get; init; } = 3.0;
    public double PackageReference { get; init; } = 2.0;
    public double PackageDependency { get; init; } = 1.0;
    public double ContractedPath { get; init; } = 0.25;
}

public sealed record CommunitySettings
{
    public string Algorithm { get; init; } = "leiden-cpm";
    public int Seed { get; init; } = 42;
    public int Trials { get; init; } = 10;
    public int Levels { get; init; } = 3;
    public double Resolution { get; init; } = 0.5;
    public int? TargetSize { get; init; } = 20;
    public int? MinSize { get; init; } = 2;
    public bool IncludeTestsInDetection { get; init; }
    public bool IncludeUnresolved { get; init; }
    public bool IncludeThirdPartyPackages { get; init; }
    public bool IncludeSystemPackages { get; init; }
    public bool IncludeUnmappedInternalPackages { get; init; }
    public IReadOnlyList<string> InternalPackagePatterns { get; init; } = [];
    public CommunityEdgeWeights EdgeWeights { get; init; } = new();

    public void Validate()
    {
        if (!string.Equals(Algorithm, "leiden-cpm", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported community algorithm '{Algorithm}'. Expected 'leiden-cpm'.");
        if (Trials <= 0) throw new ArgumentException("Community trials must be positive.");
        if (Levels <= 0 || Levels > 8) throw new ArgumentException("Community levels must be between 1 and 8.");
        if (!double.IsFinite(Resolution) || Resolution <= 0) throw new ArgumentException("Community resolution must be a positive finite number.");
        if (TargetSize is <= 0) throw new ArgumentException("Community target size must be positive.");
        if (MinSize is <= 0) throw new ArgumentException("Community minimum size must be positive.");
        foreach (var weight in new[] { EdgeWeights.ProjectReference, EdgeWeights.PackageReference, EdgeWeights.PackageDependency, EdgeWeights.ContractedPath })
            if (!double.IsFinite(weight) || weight < 0) throw new ArgumentException("Community edge weights must be finite and non-negative.");
        if (InternalPackagePatterns.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Community internal-package patterns cannot be empty.");
    }
}

public enum CommunityNodeOwnership
{
    LocalProject,
    LocalProducedPackage,
    UnmappedInternalPackage,
    SystemPackage,
    ThirdPartyPackage,
    UnresolvedExternal
}

public sealed record CommunityProjectionMetadata
{
    public string PolicyVersion { get; init; } = "";
    public string Scope { get; init; } = "unknown";
    public int DetectionVertexCount { get; init; }
    public int DetectionNodeCount { get; init; }
    public int LocalProjectCount { get; init; }
    public int LocalProducedPackageCount { get; init; }
    public int IncludedUnmappedInternalPackageCount { get; init; }
    public int IncludedSystemPackageCount { get; init; }
    public int IncludedThirdPartyPackageCount { get; init; }
    public int CollapsedProducerPairCount { get; init; }
    public int ExcludedTestProjectCount { get; init; }
    public int ExcludedSystemPackageCount { get; init; }
    public int ExcludedThirdPartyPackageCount { get; init; }
    public int ExcludedUnresolvedExternalCount { get; init; }
    public int ContractedEdgeCount { get; init; }
}

public sealed record CommunityProjectionRules
{
    public bool DirectedForDetection { get; init; }
    public bool ExcludeTests { get; init; } = true;
    public bool ExcludeUnresolved { get; init; } = true;
    public bool CollapseLocalProducerPackages { get; init; } = true;
    public bool IncludeContractedPaths { get; init; } = true;
    public bool IncludeProducerEdges { get; init; }
    public bool IncludeThirdPartyPackages { get; init; }
    public bool IncludeSystemPackages { get; init; }
    public bool IncludeUnmappedInternalPackages { get; init; }
    public string OwnershipRule { get; init; } = "source-evidence";
    public string ContractedPathContextRule { get; init; } = "same-owner-compatible-tfm-rid";
    public string OppositeEdgeRule { get; init; } = "sum-distinct-directed-relationships";
    public string ContextMultiplicityRule { get; init; } = "one-logical-edge-one-base-weight";
}

public sealed record CommunityResolutionCandidate
{
    public double Resolution { get; init; }
    public double Quality { get; init; }
    public int CommunityCount { get; init; }
    public IReadOnlyList<int> Sizes { get; init; } = [];
    public int SingletonCount { get; init; }
    public bool Connected { get; init; }
    public int WinningSeed { get; init; }
    public int Trials { get; init; }
    public double Stability { get; init; }
    public double? NeighborSimilarity { get; init; }
    public bool SelectedStandard { get; init; }
}

public sealed record CommunityNameEvidence(string Token, double Score, IReadOnlyList<string> Members);

public sealed record CommunityRecord
{
    public required string StableKey { get; init; }
    public string? ParentKey { get; init; }
    public int Depth { get; init; }
    public IReadOnlyList<string> MemberNodeIds { get; init; } = [];
    public IReadOnlyList<string> DirectChildKeys { get; init; } = [];
    public double Resolution { get; init; }
    public double Quality { get; init; }
    public double Stability { get; init; }
    public int Size { get; init; }
    public int DetectionVertexCount { get; init; }
    public int ExpandedProducerPackageCount { get; init; }
    public int ProjectCount { get; init; }
    public int PackageCount { get; init; }
    public int TestProjectCount { get; init; }
    public int RunnableCount { get; init; }
    public required string Name { get; init; }
    public double NameConfidence { get; init; }
    public IReadOnlyList<CommunityNameEvidence> NameEvidence { get; init; } = [];
    public required string Color { get; init; }
    public required string BorderColor { get; init; }
    public int InternalEdgeCount { get; init; }
    public double WeightedDensity { get; init; }
    public int OutgoingEdgeCount { get; init; }
    public int IncomingEdgeCount { get; init; }
    public double CouplingRatio { get; init; }
    public IReadOnlyList<string> RepresentativeNodeIds { get; init; } = [];
    public string RepresentativeStatus { get; init; } = "eligible-source-nodes";
    public IReadOnlyList<string> BridgeNodeIds { get; init; } = [];
    public int InternalCycleCount { get; init; }
    public int VersionSkewedPackageCount { get; init; }
}

public sealed record NodeCommunityAssignment
{
    public required string NodeId { get; init; }
    public IReadOnlyList<string> DetectedCommunityPath { get; init; } = [];
    public string AssignmentSource { get; init; } = "automatic";
    public IReadOnlyList<string> AssignmentEvidence { get; init; } = [];
    public double AssignmentScore { get; init; }
    public IReadOnlyList<string> CandidateCommunities { get; init; } = [];
}

public sealed record CrossCommunityDependency
{
    public required string SourceCommunity { get; init; }
    public required string TargetCommunity { get; init; }
    public int EdgeCount { get; init; }
    public int SourceNodeCount { get; init; }
    public int TargetNodeCount { get; init; }
    public IReadOnlyDictionary<string, int> EdgeKinds { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> RepresentativeEdgeIds { get; init; } = [];
    public int UniqueContextCount { get; init; }
}

public sealed record CommunityCycle
{
    public IReadOnlyList<string> CommunityKeys { get; init; } = [];
    public IReadOnlyList<string> SupportingEdgeIds { get; init; } = [];
}

public sealed record DependencyPathRecord
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public IReadOnlyList<string> NodeIds { get; init; } = [];
    public IReadOnlyList<string> EdgeIds { get; init; } = [];
    public bool Truncated { get; init; }
}

public sealed record CommunityAnalysis
{
    public string Version { get; init; } = "1.0";
    public string Algorithm { get; init; } = "leiden-cpm";
    public string AlgorithmVersion { get; init; } = "depgraph-leiden-cpm-1";
    public string KeyDerivationVersion { get; init; } = "anchor-v1";
    public required string GraphFingerprint { get; init; }
    public CommunitySettings Settings { get; init; } = new();
    public CommunityProjectionRules ProjectionRules { get; init; } = new();
    public CommunityProjectionMetadata Projection { get; init; } = new();
    public IReadOnlyDictionary<string, CommunityNodeOwnership> NodeOwnership { get; init; } = new Dictionary<string, CommunityNodeOwnership>();
    public IReadOnlyList<CommunityResolutionCandidate> ResolutionProfile { get; init; } = [];
    public IReadOnlyList<CommunityRecord> Communities { get; init; } = [];
    public IReadOnlyDictionary<string, NodeCommunityAssignment> NodeAssignments { get; init; } = new Dictionary<string, NodeCommunityAssignment>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GranularityAssignments { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<CrossCommunityDependency> CrossCommunityDependencies { get; init; } = [];
    public IReadOnlyList<CommunityCycle> CommunityCycles { get; init; } = [];
    public IReadOnlyList<DependencyPathRecord> RunnableImpactPaths { get; init; } = [];
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = [];
}
