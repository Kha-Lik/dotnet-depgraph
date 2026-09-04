using System.Diagnostics;
using System.Text.Json;
using DotNetDependencyGraph.Core.Algorithms.Communities;
using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Application.Filtering;
using DotNetDependencyGraph.Core.Application.Scanning;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.NuGet;
using DotNetDependencyGraph.Core.Infrastructure.Output;
using DotNetDependencyGraph.Core.Infrastructure.Processes;
using DotNetDependencyGraph.Core.Infrastructure.MSBuild;

return await ProgramEntry.RunAsync(args);

public static class ProgramEntry
{
    private const string Help = """
dotnet-depgraph — offline transitive .NET dependency explorer

Usage:
  dotnet-depgraph scan --root <path> --output <path> [options]
  dotnet-depgraph validate --root <path> [options]
  dotnet-depgraph render --graph <graph.json> --output <path> [--force] [community options]
  dotnet-depgraph export --graph <graph.json> --output <file.graphml> --format graphml

Scan options:
  --include-package <glob>       Repeatable, case-insensitive package ID glob
  --exclude-package <glob>       Repeatable; excludes take precedence
  --include-project <glob>       Repeatable root-relative project path glob
  --exclude-project <glob>       Repeatable; hides projects but retains produced packages
  --collapse-local-packages     Merge producer projects with their package nodes by default
  --filter-mode strict|contract  Default: contract
  --restore never|missing|always Default: missing
  --jobs <n>                     Restore concurrency; default: processor-aware (max 4)
  --target-framework all|<TFM>   Repeatable; default: all
  --runtime <RID>                Repeatable
  --property Name=Value          Repeatable global MSBuild/restore property
  --include-path <glob>          Repeatable root-relative path glob
  --exclude-path <glob>          Repeatable root-relative path glob
  --config <file.json>           Versioned configuration defaults; CLI wins
  --seed <integer>               Default: 42
  --community-resolution <n>    Positive CPM gamma; default: 0.5
  --community-seed <integer>    Deterministic Leiden seed; default: --seed/42
  --community-trials <n>        Seeded trials per resolution; default: 10
  --community-levels <n>        Precomputed hierarchy depth; default: 3
  --community-target-size <n>   Soft selection/diagnostic hint; default: 20
  --community-min-size <n>      Minimum useful split hint; default: 2
  --community-contracted-weight <n> Low weight for paths through excluded packages; default: 0.25
  --community-include-third-party Include third-party packages in detection
  --community-include-system-packages Include framework/system packages in detection
  --community-include-unmapped-internal-packages Include configured internal packages without a local producer
  --community-internal-package <glob> Repeatable internal package ownership pattern
  --community-overrides <json>  Initial versioned manual overrides for the report
  --fail-on-incomplete           Exit 3 when authoritative data is incomplete
  --force                        Allow known report files in a nonempty directory
  --verbosity quiet|normal|detailed Normal logs stages/results; detailed also logs each project
  --help

Glob syntax: * matches any characters and ? matches one character. Matching is
case-insensitive for package IDs. Restore/evaluation should only be used on trusted repositories.
""";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }
        using var cancellation = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            var command = args[0]; var parsed = Arguments.Parse(args[1..]);
            return command switch
            {
                "scan" => await ScanAsync(parsed, true, cancellation.Token),
                "validate" => await ScanAsync(parsed, false, cancellation.Token),
                "render" => Render(parsed),
                "export" => Export(parsed),
                _ => throw new ArgumentException($"Unknown command '{command}'.\n\n{Help}")
            };
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { Console.Error.WriteLine("Fatal: " + ex.Message); return 1; }
    }

    private static async Task<int> ScanAsync(Arguments args, bool write, CancellationToken cancellationToken)
    {
        var verbosity = Verbosity(args.One("verbosity"));
        var elapsed = Stopwatch.StartNew();
        Action<string>? progress = verbosity == "quiet" ? null : message => Console.WriteLine($"[+{elapsed.Elapsed.TotalSeconds,7:0.0}s] {message}");
        var config = LoadConfig(args.One("config"));
        var root = Path.GetFullPath(args.Required("root"));
        var output = write ? Path.GetFullPath(args.Required("output")) : null;
        var includes = args.ManyOr("include-package", config.IncludePackages);
        var excludes = args.ManyOr("exclude-package", config.ExcludePackages);
        var includeProjects = args.ManyOr("include-project", config.IncludeProjects);
        var excludeProjects = args.ManyOr("exclude-project", config.ExcludeProjects);
        var includePaths = args.ManyOr("include-path", config.IncludePaths);
        var excludePaths = args.ManyOr("exclude-path", config.ExcludePaths);
        var tfms = args.Many("target-framework"); if (tfms.Count == 0) tfms = ["all"];
        var rids = args.Many("runtime"); var properties = new Dictionary<string, string>(config.Properties, StringComparer.OrdinalIgnoreCase);
        foreach (var item in args.Many("property")) { var split = item.IndexOf('='); if (split <= 0) throw new ArgumentException("--property must be Name=Value."); properties[item[..split]] = item[(split + 1)..]; }
        var filterMode = ParseEnum(args.One("filter-mode") ?? config.FilterMode ?? "contract", FilterMode.Contract);
        var restoreMode = ParseEnum(args.One("restore") ?? config.RestoreMode ?? "missing", RestoreMode.Missing);
        var jobs = PositiveInt(args.One("jobs"), Math.Min(4, Environment.ProcessorCount));
        var seed = Int(args.One("seed"), config.Seed ?? 42);
        var communities = CommunityOptions(args, config.Communities, seed);
        var scanOptions = new ScanOptions
        {
            Root = root,
            OutputDirectory = output,
            IncludePaths = includePaths,
            ExcludePaths = excludePaths,
            TargetFrameworks = tfms,
            RuntimeIdentifiers = rids,
            GlobalProperties = properties,
            Progress = progress,
            DetailedProgress = verbosity == "detailed",
            ComputeCommunities = false,
            CommunitySettings = communities
        };
        progress?.Invoke($"Starting scan under {root}.");
        var first = new Scanner().Scan(scanOptions, cancellationToken); var restoreResults = Array.Empty<RestoreResult>();
        if (restoreMode != RestoreMode.Never)
        {
            progress?.Invoke($"Checking restore state for {first.Projects.Count} project(s) using mode '{restoreMode.ToString().ToLowerInvariant()}' and {jobs} job(s).");
            restoreResults = (await RestoreRunner.RestoreAsync(first.Projects, restoreMode, jobs, properties, cancellationToken)).ToArray();
            progress?.Invoke(restoreResults.Length == 0 ? "Restore not required; existing assets are usable." : $"Restore completed for {restoreResults.Count(x => x.ExitCode == 0)}/{restoreResults.Length} project(s); rescanning restored assets.");
        }
        var result = restoreResults.Length > 0 ? new Scanner().Scan(scanOptions, cancellationToken) : first;
        if (restoreResults.Any(x => x.ExitCode != 0))
        {
            var failed = restoreResults.Where(x => x.ExitCode != 0).ToArray(); var diagnostics = result.Graph.Diagnostics.Concat(failed.Select(x => new GraphDiagnostic("restore-failed", DiagnosticSeverity.Error, $"dotnet restore exited {x.ExitCode}: {LastLines(x.Output, 8)}", Path: x.ProjectPath))).OrderBy(x => x.Code).ThenBy(x => x.Path).ToArray();
            result = result with { Graph = result.Graph with { Diagnostics = diagnostics, Completeness = result.Graph.Completeness with { FailedRestores = failed.Length, Complete = false } } };
        }
        result = result with { Graph = ApplyConfiguration(result.Graph, config, communities, progress) };
        if (write)
        {
            var viewer = Path.Combine(AppContext.BaseDirectory, "viewer");
            if (!Directory.Exists(viewer)) throw new IOException($"Bundled viewer assets were not found at {viewer}.");
            var collapseLocalPackages = args.Flag("collapse-local-packages") || config.CollapseLocalPackages;
            progress?.Invoke($"Writing canonical graph and offline '{filterMode.ToString().ToLowerInvariant()}' report to {output}.");
            OutputWriter.Write(output!, result.Graph, new(includes, excludes, includeProjects, excludeProjects, filterMode, seed, args.Flag("force"), collapseLocalPackages, ReadOverrides(args.One("community-overrides"), result.Graph)), viewer);
            progress?.Invoke("Report files written and schema validation passed.");
        }
        var g = GraphFilter.Apply(result.Graph, includes, excludes, filterMode, includeProjects, excludeProjects);
        Console.WriteLine($"{(result.Graph.Completeness.Complete ? "Complete" : "INCOMPLETE")}: {result.Projects.Count} projects, {g.Nodes.Count(x => x.Kind == NodeKind.Package)} packages, {g.Edges.Count} displayed edges, {g.Nodes.Select(x => x.Component).Distinct().Count()} components, {result.Graph.Diagnostics.Count(x => x.Severity != DiagnosticSeverity.Info)} warnings/errors{(write ? $". Report: {Path.Combine(output!, "index.html")}" : ".")}");
        return args.Flag("fail-on-incomplete") && !result.Graph.Completeness.Complete ? 3 : 0;
    }

    private static int Export(Arguments args)
    {
        if (!string.Equals(args.One("format") ?? "graphml", "graphml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only graphml export is supported.");
        var graph = ReadGraph(Path.GetFullPath(args.Required("graph")));
        File.WriteAllText(args.Required("output"), OutputWriter.GraphMl(graph)); return 0;
    }

    private static int Render(Arguments args)
    {
        var verbosity = Verbosity(args.One("verbosity"));
        var elapsed = Stopwatch.StartNew();
        Action<string>? progress = verbosity == "quiet" ? null : message => Console.WriteLine($"[+{elapsed.Elapsed.TotalSeconds,7:0.0}s] {message}");
        var graphPath = Path.GetFullPath(args.Required("graph"));
        var output = Path.GetFullPath(args.Required("output"));
        progress?.Invoke($"Reading and validating canonical graph {graphPath}.");
        var graph = ReadGraph(graphPath);
        var viewer = Path.Combine(AppContext.BaseDirectory, "viewer");
        if (!Directory.Exists(viewer)) throw new IOException($"Bundled viewer assets were not found at {viewer}.");
        var seed = Int(args.One("seed"), 42);
        var communities = CommunityOptions(args, graph.CommunityAnalysis?.Settings ?? new(), seed);
        if (graph.CommunityAnalysis is null || graph.CommunityAnalysis.Projection.PolicyVersion != CommunityProjectionBuilder.CurrentPolicyVersion
            || args.HasAny("community-resolution", "community-seed", "community-trials", "community-levels", "community-target-size", "community-min-size", "community-contracted-weight",
                "community-include-third-party", "community-include-system-packages", "community-include-unmapped-internal-packages", "community-internal-package"))
            graph = GraphAnalysis.Analyze(graph with { CommunityAnalysis = null }, communities.Seed, communities, progress: progress);
        else progress?.Invoke("Reusing compatible embedded community analysis; pass a community option to recompute it.");
        var filterMode = ParseEnum(args.One("filter-mode") ?? "contract", FilterMode.Contract);
        progress?.Invoke($"Writing offline '{filterMode.ToString().ToLowerInvariant()}' report to {output}.");
        OutputWriter.Write(output, graph, new(args.Many("include-package"), args.Many("exclude-package"), args.Many("include-project"), args.Many("exclude-project"), filterMode, seed, args.Flag("force"), args.Flag("collapse-local-packages"), ReadOverrides(args.One("community-overrides"), graph)), viewer);
        progress?.Invoke("Report files written and schema validation passed.");
        Console.WriteLine($"Rendered {graph.Nodes.Count} nodes and {graph.Edges.Count} edges without scanning or restore. Report: {Path.Combine(output, "index.html")}");
        return 0;
    }

    private static DependencyGraph ReadGraph(string path)
    {
        using var stream = File.OpenRead(path);
        var graph = JsonSerializer.Deserialize<DependencyGraph>(stream, OutputWriter.JsonOptions) ?? throw new InvalidDataException("Invalid graph JSON.");
        GraphSchema.Validate(graph);
        return graph;
    }
    private static ToolConfig LoadConfig(string? path)
    {
        if (path is null) return new();
        var config = JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(Path.GetFullPath(path)), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        if (config.SchemaVersion != "1.0") throw new ArgumentException($"Unsupported config schemaVersion '{config.SchemaVersion}'."); return config;
    }
    private static DependencyGraph ApplyConfiguration(DependencyGraph graph, ToolConfig config, CommunitySettings communities, Action<string>? progress = null)
    {
        var changesGraph = config.PackageAliases.Count > 0 || config.ProjectRules.Length > 0 || config.KnownPackageProducers.Count > 0;
        if (!changesGraph && graph.CommunityAnalysis?.Settings == communities) { progress?.Invoke("Reusing compatible community analysis."); return graph; }
        progress?.Invoke(changesGraph ? "Applying configured graph metadata and computing community analysis." : "Computing community analysis from the canonical graph.");
        var nodes = graph.Nodes.Select(node =>
        {
            var alias = node.Kind == NodeKind.Package ? config.PackageAliases.FirstOrDefault(x => x.Key.Equals(node.Label, StringComparison.OrdinalIgnoreCase)).Value ?? node.Label : node.Label;
            var rule = node.Kind == NodeKind.Project ? config.ProjectRules.FirstOrDefault(x => node.Path is not null && Glob.IsMatch(node.Path, x.PathGlob)) : null;
            return node with { Label = rule?.Label ?? alias, Classification = rule?.Category ?? node.Classification, ColorHint = rule?.ColorHint ?? node.ColorHint, Tags = rule?.Tag is null ? node.Tags : node.Tags.Append(rule.Tag).Distinct().Order().ToArray(), ClassificationEvidence = rule is null ? node.ClassificationEvidence : node.ClassificationEvidence.Append("configuration override: " + rule.PathGlob).ToArray() };
        }).ToList();
        var edges = graph.Edges.ToList();
        foreach (var mapping in config.KnownPackageProducers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var packageId = LockFileExtractor.PackageId(mapping.Key); var projectId = "project:" + ProjectDiscovery.Normalize(mapping.Value); var project = nodes.FirstOrDefault(x => x.Id == projectId);
            if (project is null) continue; if (nodes.All(x => x.Id != packageId)) nodes.Add(new() { Id = packageId, Label = mapping.Key, Kind = NodeKind.Package });
            if (edges.All(x => !(x.Source == projectId && x.Target == packageId && x.Kind == EdgeKind.ProducesPackage))) edges.Add(new() { Id = $"edge:{EdgeKind.ProducesPackage}:{projectId}->{packageId}", Source = projectId, Target = packageId, Kind = EdgeKind.ProducesPackage, Contexts = [new(projectId, "", "", null, null, null, false)] });
        }
        return GraphAnalysis.Analyze(graph with { Nodes = nodes.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(), Edges = edges.OrderBy(x => x.Source).ThenBy(x => x.Target).ThenBy(x => x.Kind).ToArray(), CommunityAnalysis = null }, communities.Seed, communities, progress: progress);
    }
    private static CommunitySettings CommunityOptions(Arguments args, CommunitySettings configured, int seed) => configured with
    {
        Seed = Int(args.One("community-seed"), configured.Seed == 42 ? seed : configured.Seed),
        Trials = PositiveInt(args.One("community-trials"), configured.Trials, "--community-trials"),
        Levels = PositiveInt(args.One("community-levels"), configured.Levels, "--community-levels"),
        Resolution = PositiveDouble(args.One("community-resolution"), configured.Resolution, "--community-resolution"),
        TargetSize = OptionalPositiveInt(args.One("community-target-size"), configured.TargetSize, "--community-target-size"),
        MinSize = OptionalPositiveInt(args.One("community-min-size"), configured.MinSize, "--community-min-size"),
        IncludeThirdPartyPackages = args.Flag("community-include-third-party") || configured.IncludeThirdPartyPackages,
        IncludeSystemPackages = args.Flag("community-include-system-packages") || configured.IncludeSystemPackages,
        IncludeUnmappedInternalPackages = args.Flag("community-include-unmapped-internal-packages") || configured.IncludeUnmappedInternalPackages,
        InternalPackagePatterns = args.ManyOr("community-internal-package", configured.InternalPackagePatterns),
        EdgeWeights = configured.EdgeWeights with { ContractedPath = NonNegativeDouble(args.One("community-contracted-weight"), configured.EdgeWeights.ContractedPath, "--community-contracted-weight") }
    };
    private static CommunityOverrideDocument? ReadOverrides(string? path, DependencyGraph graph)
    {
        if (path is null) return null;
        var document = JsonSerializer.Deserialize<CommunityOverrideDocument>(File.ReadAllText(Path.GetFullPath(path)), OutputWriter.JsonOptions) ?? throw new ArgumentException("Invalid community override JSON.");
        if (document.Version != 1) throw new ArgumentException($"Unsupported community override version '{document.Version}'. Expected 1.");
        var analysis = graph.CommunityAnalysis!;
        if (string.Equals(document.GraphFingerprint, analysis.GraphFingerprint, StringComparison.Ordinal)) return document;
        var diagnostics = new List<GraphDiagnostic>(); var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var standard = analysis.GranularityAssignments["standard"];
        var styles = new List<CommunityStyleOverride>();
        foreach (var style in document.CommunityOverrides)
        {
            if (standard.TryGetValue(style.DetectedAnchorNodeId, out var current))
            { if (style.CommunityKey is not null) keyMap[style.CommunityKey] = current; styles.Add(style with { CommunityKey = current }); }
            else diagnostics.Add(new("community-override-stale", DiagnosticSeverity.Info, "Community style override anchor is absent from the rescanned graph.", style.DetectedAnchorNodeId));
        }
        var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal); var manualIds = document.ManualCommunities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in document.NodeAssignments)
        {
            var target = keyMap.GetValueOrDefault(assignment.Value, assignment.Value);
            if (nodeIds.Contains(assignment.Key) && (manualIds.Contains(target) || analysis.Communities.Any(community => community.StableKey == target))) assignments[assignment.Key] = target;
            else diagnostics.Add(new("community-override-stale", DiagnosticSeverity.Info, "Node assignment could not be matched safely after rescanning.", assignment.Key));
        }
        Console.Error.WriteLine($"Community override fingerprint changed: matched {styles.Count} styles and {assignments.Count} node assignments; {diagnostics.Count} stale entries were ignored.");
        return document with { GraphFingerprint = analysis.GraphFingerprint, CommunityOverrides = styles, NodeAssignments = assignments, OverrideDiagnostics = diagnostics };
    }
    private static T ParseEnum<T>(string value, T _) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ArgumentException($"Invalid {typeof(T).Name} value '{value}'.");
    private static int PositiveInt(string? value, int fallback, string option = "--jobs") { var v = Int(value, fallback); return v > 0 ? v : throw new ArgumentException($"{option} must be positive."); }
    private static int? OptionalPositiveInt(string? value, int? fallback, string option) { if (value is null) return fallback; var parsed = Int(value, 0); return parsed > 0 ? parsed : throw new ArgumentException($"{option} must be positive."); }
    private static double PositiveDouble(string? value, double fallback, string option) => value is null ? fallback : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed > 0 ? parsed : throw new ArgumentException($"{option} must be a positive number.");
    private static double NonNegativeDouble(string? value, double fallback, string option) => value is null ? fallback : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed >= 0 ? parsed : throw new ArgumentException($"{option} must be a non-negative number.");
    private static int Int(string? value, int fallback) => value is null ? fallback : int.TryParse(value, out var v) ? v : throw new ArgumentException($"Expected an integer, got '{value}'.");
    private static string Verbosity(string? value) => (value ?? "normal").ToLowerInvariant() is var parsed && parsed is "quiet" or "normal" or "detailed" ? parsed : throw new ArgumentException("--verbosity must be quiet, normal, or detailed.");
    private static string LastLines(string value, int count) => string.Join(" | ", value.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(count)).Trim();
}

internal sealed record ToolConfig
{
    public string SchemaVersion { get; init; } = "1.0"; public string[] IncludePackages { get; init; } = []; public string[] ExcludePackages { get; init; } = [];
    public string[] IncludePaths { get; init; } = []; public string[] ExcludePaths { get; init; } = [];
    public string[] IncludeProjects { get; init; } = []; public string[] ExcludeProjects { get; init; } = []; public string? FilterMode { get; init; }
    public string? RestoreMode { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new(); public int? Seed { get; init; }
    public bool CollapseLocalPackages { get; init; }
    public CommunitySettings Communities { get; init; } = new();
    public ProjectRule[] ProjectRules { get; init; } = []; public Dictionary<string, string> PackageAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase); public Dictionary<string, string> KnownPackageProducers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
internal sealed record ProjectRule { public required string PathGlob { get; init; } public string? Category { get; init; } public string? Label { get; init; } public string? Tag { get; init; } public string? ColorHint { get; init; } }

internal sealed class Arguments
{
    private readonly Dictionary<string, List<string?>> _values = new(StringComparer.OrdinalIgnoreCase);
    public static Arguments Parse(string[] args) { var p = new Arguments(); for (var i = 0; i < args.Length; i++) { if (!args[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{args[i]}'."); var key = args[i][2..]; string? value = null; if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) value = args[++i]; if (!p._values.TryGetValue(key, out var list)) p._values[key] = list = []; list.Add(value); } return p; }
    public string Required(string name) => One(name) ?? throw new ArgumentException($"Missing required --{name}.");
    public string? One(string name) { if (!_values.TryGetValue(name, out var values)) return null; if (values.Count > 1) throw new ArgumentException($"--{name} may only be specified once."); return values[0] ?? throw new ArgumentException($"--{name} requires a value."); }
    public List<string> Many(string name) => _values.TryGetValue(name, out var values) ? values.Select(x => x ?? throw new ArgumentException($"--{name} requires a value.")).ToList() : [];
    public IReadOnlyList<string> ManyOr(string name, IReadOnlyList<string> fallback) { var values = Many(name); return values.Count > 0 ? values : fallback; }
    public bool Flag(string name) => _values.TryGetValue(name, out var values) ? values.All(x => x is null) ? true : throw new ArgumentException($"--{name} does not take a value.") : false;
    public bool HasAny(params string[] names) => names.Any(_values.ContainsKey);
}
