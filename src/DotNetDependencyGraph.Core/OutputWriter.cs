using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetDependencyGraph.Core;

public sealed record ReportOptions(
    IReadOnlyList<string> IncludePackages,
    IReadOnlyList<string> ExcludePackages,
    IReadOnlyList<string> IncludeProjects,
    IReadOnlyList<string> ExcludeProjects,
    FilterMode FilterMode,
    int Seed = 42,
    bool Force = false,
    bool CollapseLocalPackages = false,
    CommunityOverrideDocument? CommunityOverrides = null);

public static class OutputWriter
{
    private static readonly string[] KnownFiles = ["index.html", "graph.json", "diagnostics.json", "summary.md", "graph.graphml", "viewer.js", "viewer.css", "cytoscape.min.js", "d3-dispatch.min.js", "d3-quadtree.min.js", "d3-timer.min.js", "d3-force.min.js", "THIRD-PARTY-NOTICES.txt"];
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static void Write(string output, DependencyGraph raw, ReportOptions options, string viewerDirectory)
    {
        if (raw.CommunityAnalysis is null) raw = GraphAnalysis.Analyze(raw, options.Seed);
        GraphSchema.Validate(raw);
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var unknown = Directory.EnumerateFileSystemEntries(output).Where(x => !KnownFiles.Contains(Path.GetFileName(x), StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0 && !options.Force) throw new IOException($"Output directory is not empty. Use --force to write known report files alongside existing content: {output}");
        var strict = GraphFilter.Apply(raw, options.IncludePackages, options.ExcludePackages, FilterMode.Strict, options.IncludeProjects, options.ExcludeProjects);
        var contract = GraphFilter.Apply(raw, options.IncludePackages, options.ExcludePackages, FilterMode.Contract, options.IncludeProjects, options.ExcludeProjects);
        WriteJson(Path.Combine(output, "graph.json"), raw);
        WriteJson(Path.Combine(output, "diagnostics.json"), new { schemaVersion = "2.0", completeness = raw.Completeness, diagnostics = raw.Diagnostics.Concat(raw.CommunityAnalysis?.Diagnostics ?? []) });
        File.WriteAllText(Path.Combine(output, "graph.graphml"), GraphMl(raw), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "summary.md"), Summary(raw, options.FilterMode == FilterMode.Strict ? strict : contract), new UTF8Encoding(false));
        CopyAsset(viewerDirectory, output, "viewer.js"); CopyAsset(viewerDirectory, output, "viewer.css");
        CopyAsset(viewerDirectory, output, "cytoscape.min.js");
        foreach (var asset in new[] { "d3-dispatch.min.js", "d3-quadtree.min.js", "d3-timer.min.js", "d3-force.min.js" }) CopyAsset(viewerDirectory, output, asset);
        File.Copy(Path.Combine(viewerDirectory, "THIRD-PARTY-NOTICES.txt"), Path.Combine(output, "THIRD-PARTY-NOTICES.txt"), true);
        var physics = new PhysicsDefaults();
        var viewsEqualRaw = options.IncludePackages.Count == 0 && options.ExcludePackages.Count == 0
            && options.IncludeProjects.Count == 0 && options.ExcludeProjects.Count == 0;
        var payload = JsonSerializer.Serialize(new { raw, strict = viewsEqualRaw ? null : strict, contract = viewsEqualRaw ? null : contract, communityOverrides = options.CommunityOverrides, defaults = new { filterMode = Kebab(options.FilterMode.ToString()), seed = options.Seed, collapseLocalPackages = options.CollapseLocalPackages, physics, physicsStorageKey = PhysicsDefaults.StorageKey, importantLabelCount = 12, viewerStorageKey = "dotnet-depgraph.viewer.v1", communityOverrideStoragePrefix = "dotnet-depgraph.communities.v1" } }, JsonOptions);
        var html = """<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>.NET Dependency Graph</title><link rel="stylesheet" href="viewer.css"></head><body><div id="warning" hidden></div><header><strong>.NET Dependency Graph</strong><input id="search" placeholder="Search nodes…" aria-label="Search"><button id="fit" title="Fit every visible graph island in the viewport">Fit all</button><button id="reset">Reset view</button><button id="explain-path">Explain path</button><button id="png">PNG</button><button id="json">Displayed JSON</button><span id="physics-status" role="status">Physics idle</span></header><aside id="filters"><label>View <select id="view"><option value="contract">Contracted</option><option value="strict">Strict</option><option value="raw">Raw</option></select></label><label><input id="collapse" type="checkbox"> Collapse local packages</label><label>Community granularity <select id="granularity"><option value="coarse">Coarse</option><option value="standard" selected>Standard</option><option value="fine">Fine</option></select></label><label>Color by <select id="color-mode"><option value="community">Effective community</option><option value="kind">Node kind</option></select></label><label>Size by <select id="size-metric"><option value="transitive">All transitive dependents</option><option value="runnable">Runnable dependents</option></select></label><label>Minimum runnable dependents <input id="runnable-min" type="number" min="0" step="1" value="0"></label><label>Neighborhood <select id="hops"><option value="0">All</option><option>1</option><option>2</option><option>3</option></select></label><label>Kind <select id="kind"><option value="">All</option></select></label><label>Edge <select id="edgeKind"><option value="">All</option></select></label><label>Component <select id="component"><option value="">All</option></select></label><label>Community <select id="community"><option value="">All</option></select></label><label>TFM <select id="tfm"><option value="">All</option></select></label><label>RID <select id="rid"><option value="">All</option></select></label><label><input id="skew" type="checkbox"> Version skew only</label><details id="labels"><summary>Labels</summary><label title="Always label this many visually largest nodes">Always show largest <output id="important-label-count-value"></output><input id="important-label-count" type="range" min="0" max="50" step="1"></label></details><details id="physics"><summary>Physics / Layout</summary><label title="How strongly every node pushes other nodes away">Repulsion <output id="repulsion-value"></output><input id="repulsion" type="range" min="100" max="5000" step="50"></label><label title="The ideal spring length between linked nodes">Link distance <output id="link-distance-value"></output><input id="link-distance" type="range" min="30" max="240" step="5"></label><label title="How strongly edge springs pull linked nodes together">Link strength <output id="link-strength-value"></output><input id="link-strength" type="range" min="0.02" max="1" step="0.01"></label><label title="Empty space added outside each node's rendered radius">Node spacing <output id="collision-padding-value"></output><input id="collision-padding" type="range" min="2" max="50" step="1"></label><label title="Weak attraction toward the center of each graph island">Gravity <output id="gravity-value"></output><input id="gravity" type="range" min="0" max="0.2" step="0.005"></label><label title="Weak attraction toward the effective community centroid within each graph island">Community attraction <output id="community-attraction-value"></output><input id="community-attraction" type="range" min="0" max="0.08" step="0.002"></label><div class="physics-actions"><button id="pause">Pause physics</button><button id="rerun">Re-run layout</button><button id="physics-reset">Reset defaults</button></div></details><section><b>Direction</b><p>Arrows point from a consumer to its dependency.</p><p><span class="line project"></span> project · <span class="line package"></span> package · <span class="line contracted"></span> contracted · <span class="line producer"></span> producer</p></section><details id="community-legend" open><summary>Communities</summary><input id="community-search" placeholder="Search communities…" aria-label="Search communities"><div id="community-legend-rows"></div><div class="community-actions"><button id="community-create">Create from selection</button><button id="community-merge">Merge selected</button><button id="community-export">Export overrides</button><button id="community-import">Import overrides</button><button id="community-effective">Effective mapping</button><button id="community-reset">Reset all</button><input id="community-import-file" type="file" accept="application/json" hidden></div><p class="legend-help">Fill and border colors together identify the effective community. Dashed border = manual assignment; thicker border = bridge/shared role; red outer glow = version skew. Communities are dependency-topology structure, not guaranteed business domains.</p></details><section id="components"><b>Components</b></section></aside><main id="cy"></main><aside id="details"><h2>Selection</h2><p>Select a node to inspect its metadata and immediate relationships.</p></aside><script id="graph-data" type="application/json">__DATA__</script><script src="cytoscape.min.js"></script><script src="d3-dispatch.min.js"></script><script src="d3-quadtree.min.js"></script><script src="d3-timer.min.js"></script><script src="d3-force.min.js"></script><script src="viewer.js"></script></body></html>""".Replace("__DATA__", payload, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(output, "index.html"), html, new UTF8Encoding(false));
    }

    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    private static void CopyAsset(string source, string output, string name) => File.Copy(Path.Combine(source, name), Path.Combine(output, name), true);
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.Default };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)); return options;
    }
    private static string Kebab(string value) => JsonNamingPolicy.KebabCaseLower.ConvertName(value);

    public static string GraphMl(DependencyGraph graph)
    {
        static string E(string value) => SecurityElement.Escape(value) ?? "";
        var b = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<graphml xmlns=\"http://graphml.graphdrawing.org/xmlns\"><key id=\"label\" for=\"node\" attr.name=\"label\" attr.type=\"string\"/><key id=\"kind\" for=\"all\" attr.name=\"kind\" attr.type=\"string\"/><key id=\"community\" for=\"node\" attr.name=\"detectedCommunity\" attr.type=\"string\"/><graph id=\"dependency-graph\" edgedefault=\"directed\">\n");
        var standard = graph.CommunityAnalysis?.GranularityAssignments.GetValueOrDefault("standard");
        foreach (var n in graph.Nodes) b.Append("<node id=\"").Append(E(n.Id)).Append("\"><data key=\"label\">").Append(E(n.Label)).Append("</data><data key=\"kind\">").Append(Kebab(n.Kind.ToString())).Append("</data><data key=\"community\">").Append(E(standard?.GetValueOrDefault(n.Id) ?? "")).Append("</data></node>\n");
        foreach (var e in graph.Edges) b.Append("<edge id=\"").Append(E(e.Id)).Append("\" source=\"").Append(E(e.Source)).Append("\" target=\"").Append(E(e.Target)).Append("\"><data key=\"kind\">").Append(Kebab(e.Kind.ToString())).Append("</data></edge>\n");
        return b.Append("</graph></graphml>\n").ToString();
    }

    private static string Summary(DependencyGraph raw, DependencyGraph display)
    {
        var components = display.Nodes.GroupBy(x => x.Component).OrderByDescending(x => x.Count());
        var b = new StringBuilder("# Dependency graph summary\n\n");
        if (!raw.Completeness.Complete) b.Append("> **Incomplete:** some projects could not be represented authoritatively. See `diagnostics.json`.\n\n");
        b.AppendLine($"- Raw graph: {raw.Nodes.Count} nodes, {raw.Edges.Count} edges");
        b.AppendLine($"- Display graph: {display.Nodes.Count} nodes, {display.Edges.Count} edges ({display.Edges.Count(x => x.Derived)} contracted)");
        b.AppendLine($"- Projects discovered/evaluated/with assets: {raw.Completeness.DiscoveredProjects}/{raw.Completeness.EvaluatedProjects}/{raw.Completeness.ProjectsWithValidAssets}");
        b.AppendLine($"- Components: {components.Count()}; isolated display nodes: {display.Nodes.Count(x => x.InDegree == 0 && x.OutDegree == 0)}");
        b.AppendLine($"- Warnings/errors: {raw.Diagnostics.Count(x => x.Severity == DiagnosticSeverity.Warning)}/{raw.Diagnostics.Count(x => x.Severity == DiagnosticSeverity.Error)}");
        b.AppendLine($"- Frameworks:{(raw.TargetFrameworks.Count > 0 ? " " + string.Join(", ", raw.TargetFrameworks) : "")}");
        b.AppendLine($"- RIDs:{(raw.RuntimeIdentifiers.Count > 0 ? " " + string.Join(", ", raw.RuntimeIdentifiers) : "")}");
        if (raw.CommunityAnalysis is { } analysis)
        {
            var selected = analysis.ResolutionProfile.SingleOrDefault(candidate => candidate.SelectedStandard);
            var standardCount = analysis.GranularityAssignments["standard"].Values.Distinct(StringComparer.Ordinal).Count();
            b.AppendLine($"- Communities: Leiden/CPM; standard resolution {selected?.Resolution}; {standardCount} strict-hierarchy groups; trial stability {selected?.Stability:0.###}");
            b.AppendLine($"- Community projection: project/package weights {analysis.Settings.EdgeWeights.ProjectReference:0.###}/{analysis.Settings.EdgeWeights.PackageReference:0.###}/{analysis.Settings.EdgeWeights.PackageDependency:0.###}; tests excluded: {analysis.ProjectionRules.ExcludeTests}; producer pairs collapsed: {analysis.ProjectionRules.CollapseLocalProducerPackages}");
        }
        b.AppendLine();
        b.AppendLine("## Components\n");
        foreach (var c in components) b.AppendLine($"- Component {c.Key}: {c.Count()} nodes; representative: {string.Join(", ", c.OrderByDescending(x => x.Centrality).ThenBy(x => x.Label).Take(3).Select(x => x.Label))}");
        return b.ToString();
    }
}
