namespace DotNetDependencyGraph.Core;

public enum NodeKind { Project, Package, UnresolvedProject, UnresolvedLibrary }
public enum EdgeKind { ProjectReference, PackageReference, PackageDependency, ProducesPackage, ContractedPath }
public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record EdgeContext(
    string OwnerProjectId, string AssetsFile, string TargetFramework, string? RuntimeIdentifier,
    string? RequestedVersion, string? ResolvedVersion, bool Direct, int ObservationCount = 1);

public sealed record GraphNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required NodeKind Kind { get; init; }
    public string? Path { get; init; }
    public string? Classification { get; init; }
    public IReadOnlyList<string> ClassificationEvidence { get; init; } = [];
    public IReadOnlyList<string> Versions { get; init; } = [];
    public bool VersionSkew { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];
    public int Component { get; init; } = -1;
    public int Community { get; init; } = -1;
    public int InDegree { get; init; }
    public int OutDegree { get; init; }
    public int DirectDependencies { get; init; }
    public int TransitiveDependencies { get; init; }
    public int DirectDependents { get; init; }
    public int TransitiveDependents { get; init; }
    public double Centrality { get; init; }
    public bool InCycle { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? ColorHint { get; init; }
}

public sealed record GraphEdge
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required EdgeKind Kind { get; init; }
    public IReadOnlyList<EdgeContext> Contexts { get; init; } = [];
    public bool Derived { get; init; }
    public int? MinimumHiddenHops { get; init; }
    public int? PathCount { get; init; }
    public bool PathCountLimited { get; init; }
    public IReadOnlyList<IReadOnlyList<string>> HiddenPathSamples { get; init; } = [];
}

public sealed record GraphDiagnostic(
    string Code, DiagnosticSeverity Severity, string Message, string? ProjectId = null, string? Path = null);

public sealed record GraphCompleteness
{
    public int DiscoveredProjects { get; init; }
    public int EvaluatedProjects { get; init; }
    public int ProjectsWithValidAssets { get; init; }
    public int MissingAssets { get; init; }
    public int MalformedAssets { get; init; }
    public int MismatchedAssets { get; init; }
    public int FailedRestores { get; init; }
    public bool Complete { get; init; }
}

public sealed record DependencyGraph
{
    public string SchemaVersion { get; init; } = "1.0";
    public required string Root { get; init; }
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = [];
    public GraphCompleteness Completeness { get; init; } = new();
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];
}

public sealed record ProjectMetadata
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string AssemblyName { get; init; }
    public required string PackageId { get; init; }
    public bool IsPackable { get; init; }
    public string OutputType { get; init; } = "Library";
    public string Sdk { get; init; } = "";
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public required string AssetsFile { get; init; }
    public string Classification { get; init; } = "unknown-project";
    public IReadOnlyList<string> ClassificationEvidence { get; init; } = [];
}

public sealed record ScanOptions
{
    public required string Root { get; init; }
    public string? OutputDirectory { get; init; }
    public IReadOnlyList<string> IncludePaths { get; init; } = [];
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
    public IReadOnlyList<string> TargetFrameworks { get; init; } = ["all"];
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];
    public IReadOnlyDictionary<string, string> GlobalProperties { get; init; } = new Dictionary<string, string>();
}
