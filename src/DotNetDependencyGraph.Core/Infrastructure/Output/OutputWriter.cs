using System.Security;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Application.Filtering;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Reporting.Rendering;

namespace DotNetDependencyGraph.Core.Infrastructure.Output;

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
    private static readonly string[] KnownFiles = ["index.html", "graph.json", "diagnostics.json", "summary.md", "graph.graphml", "icons.js", "viewer.js", "viewer.css", "manual-geometry.js", "manual-state.js", "manual-storage.js", "manual-layout.js", "manual-view.js", "coloris.min.js", "coloris.min.css", "cytoscape.min.js", "d3-dispatch.min.js", "d3-quadtree.min.js", "d3-timer.min.js", "d3-force.min.js", "THIRD-PARTY-NOTICES.txt"];
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
        WriteJson(Path.Combine(output, "diagnostics.json"), new
        {
            schemaVersion = "2.0",
            completeness = raw.Completeness,
            communityProjection = raw.CommunityAnalysis?.Projection,
            communityOwnershipCounts = raw.CommunityAnalysis?.NodeOwnership.Values.GroupBy(ownership => ownership).OrderBy(group => group.Key).ToDictionary(group => Kebab(group.Key.ToString()), group => group.Count()),
            diagnostics = raw.Diagnostics.Concat(raw.CommunityAnalysis?.Diagnostics ?? [])
        });
        File.WriteAllText(Path.Combine(output, "graph.graphml"), GraphMl(raw), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "summary.md"), Summary(raw, options.FilterMode == FilterMode.Strict ? strict : contract, viewerDirectory), new UTF8Encoding(false));
        CopyAsset(viewerDirectory, output, "icons.js"); CopyAsset(viewerDirectory, output, "viewer.js"); CopyAsset(viewerDirectory, output, "viewer.css");
        foreach (var asset in new[] { "manual-geometry.js", "manual-state.js", "manual-storage.js", "manual-layout.js", "manual-view.js" }) CopyAsset(viewerDirectory, output, asset);
        foreach (var asset in new[] { "coloris.min.js", "coloris.min.css" }) CopyAsset(viewerDirectory, output, asset);
        CopyAsset(viewerDirectory, output, "cytoscape.min.js");
        foreach (var asset in new[] { "d3-dispatch.min.js", "d3-quadtree.min.js", "d3-timer.min.js", "d3-force.min.js" }) CopyAsset(viewerDirectory, output, asset);
        File.Copy(Path.Combine(viewerDirectory, "THIRD-PARTY-NOTICES.txt"), Path.Combine(output, "THIRD-PARTY-NOTICES.txt"), true);
        var physics = new PhysicsDefaults();
        var viewsEqualRaw = options.IncludePackages.Count == 0 && options.ExcludePackages.Count == 0
            && options.IncludeProjects.Count == 0 && options.ExcludeProjects.Count == 0;
        var payload = JsonSerializer.Serialize(new { raw, strict = viewsEqualRaw ? null : strict, contract = viewsEqualRaw ? null : contract, communityOverrides = options.CommunityOverrides, defaults = new { filterMode = Kebab(options.FilterMode.ToString()), seed = options.Seed, collapseLocalPackages = options.CollapseLocalPackages, physics, physicsStorageKey = PhysicsDefaults.StorageKey, importantLabelCount = 12, viewerStorageKey = "dotnet-depgraph.viewer.v1", communityOverrideStoragePrefix = "dotnet-depgraph.communities.v1" } }, JsonOptions);
        var html = """<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>.NET Dependency Graph</title><link rel="stylesheet" href="viewer.css"></head><body><div id="warning" hidden></div><header><strong>.NET Dependency Graph</strong><nav class="view-tabs" aria-label="Graph views"><button id="tab-explore" aria-selected="true">Explore</button><button id="tab-manual" aria-selected="false">Manual layout</button></nav><div id="explore-toolbar"><input id="search" placeholder="Search nodes…" aria-label="Search"><button id="fit" title="Fit every visible graph island in the viewport">Fit all</button><button id="reset">Reset view</button><button id="explain-path">Explain path</button><button id="png">PNG</button><button id="json">Displayed JSON</button><span id="physics-status" role="status">Physics idle</span></div><div id="manual-toolbar"><button id="manual-new-group">New group</button><select id="manual-assign-target" aria-label="Assignment target"></select><button id="manual-assign">Assign to…</button><button id="manual-undo">Undo</button><button id="manual-redo">Redo</button><button id="manual-run">Run layout</button><button id="manual-pause">Pause</button><button id="manual-arrange">Arrange groups</button><button id="manual-save">Save layout…</button><button id="manual-load">Load layout…</button><input id="manual-load-file" type="file" accept="application/json" hidden><span id="manual-save-status" role="status"></span></div></header><aside id="filters"><label>View <select id="view"><option value="contract">Contracted</option><option value="strict">Strict</option><option value="raw">Raw</option></select></label><label><input id="collapse" type="checkbox"> Collapse local packages</label><label>Community granularity <select id="granularity"><option value="coarse">Coarse</option><option value="standard" selected>Standard</option><option value="fine">Fine</option></select></label><label>Color by <select id="color-mode"><option value="community">Effective community</option><option value="kind">Node kind</option></select></label><label>Size by <select id="size-metric"><option value="transitive">All transitive dependents</option><option value="runnable">Runnable dependents</option></select></label><label>Minimum runnable dependents <input id="runnable-min" type="number" min="0" step="1" value="0"></label><label>Neighborhood <select id="hops"><option value="0">All</option><option>1</option><option>2</option><option>3</option></select></label><label>Kind <select id="kind"><option value="">All</option></select></label><label>Edge <select id="edgeKind"><option value="">All</option></select></label><label>Component <select id="component"><option value="">All</option></select></label><label>Community <select id="community"><option value="">All</option></select></label><label>TFM <select id="tfm"><option value="">All</option></select></label><label>RID <select id="rid"><option value="">All</option></select></label><label><input id="skew" type="checkbox"> Version skew only</label><details id="labels"><summary>Labels</summary><label title="Always label this many visually largest nodes">Always show largest <output id="important-label-count-value"></output><input id="important-label-count" type="range" min="0" max="50" step="1"></label></details><details id="physics"><summary>Physics / Layout</summary><label title="How strongly every node pushes other nodes away">Repulsion <output id="repulsion-value"></output><input id="repulsion" type="range" min="100" max="5000" step="50"></label><label title="The ideal spring length between linked nodes">Link distance <output id="link-distance-value"></output><input id="link-distance" type="range" min="30" max="240" step="5"></label><label title="How strongly edge springs pull linked nodes together">Link strength <output id="link-strength-value"></output><input id="link-strength" type="range" min="0.02" max="1" step="0.01"></label><label title="Empty space added outside each node's rendered radius">Node spacing <output id="collision-padding-value"></output><input id="collision-padding" type="range" min="2" max="50" step="1"></label><label title="Weak attraction toward the center of each graph island">Gravity <output id="gravity-value"></output><input id="gravity" type="range" min="0" max="0.2" step="0.005"></label><label title="Weak attraction toward the effective community centroid within each graph island">Community attraction <output id="community-attraction-value"></output><input id="community-attraction" type="range" min="0" max="0.08" step="0.002"></label><div class="physics-actions"><button id="pause">Pause physics</button><button id="rerun">Re-run layout</button><button id="physics-reset">Reset defaults</button></div></details><section><b>Direction</b><p>Arrows point from a consumer to its dependency.</p><p><span class="line project"></span> project · <span class="line package"></span> package · <span class="line contracted"></span> contracted · <span class="line producer"></span> producer</p></section><details id="community-legend" open><summary>Communities</summary><input id="community-search" placeholder="Search communities…" aria-label="Search communities"><div id="community-legend-rows"></div><div class="community-actions"><button id="community-create">Create from selection</button><button id="community-merge">Merge selected</button><button id="community-export">Export overrides</button><button id="community-import">Import overrides</button><button id="community-effective">Effective mapping</button><button id="community-reset">Reset all</button><input id="community-import-file" type="file" accept="application/json" hidden></div><p class="legend-help">Fill and border colors together identify the effective community. Dashed border = manual assignment; thicker border = bridge/shared role; red outer glow = version skew. Communities are dependency-topology structure, not guaranteed business domains.</p></details><section id="components"><b>Components</b></section></aside><aside id="manual-groups"><h2>Groups</h2><div id="manual-group-list"></div><h3>Muted nodes</h3><div id="manual-muted-list"></div><button id="manual-restore-all">Restore all</button><p class="legend-help">Manual membership is semantic and does not change dependency facts or automatic communities.</p></aside><main id="canvas"><svg id="manual-regions" aria-label="Manual group regions"></svg><div id="cy"></div><section id="manual-empty" hidden><div id="manual-empty-copy"><h2>Create a manual layout</h2><p>Groups will be separated into regions. Your Explore layout is preserved.</p><button id="manual-create-current">Preview current groups</button><button id="manual-create-unassigned">Preview unassigned</button></div><div id="manual-preview-actions" hidden><p>Review the region layout before replacing the saved board.</p><button id="manual-preview-accept">Create board</button><button id="manual-preview-cancel">Cancel</button></div></section></main><aside id="details"><h2>Selection</h2><p>Select a node to inspect its metadata and immediate relationships.</p></aside><aside id="manual-inspector"><div id="manual-workspace" hidden><h2>Selection</h2><p id="manual-selection-summary">Select nodes or a region.</p><div class="manual-actions"><button id="manual-pin">Pin</button><button id="manual-unpin">Unpin</button><button id="manual-mute" title="Hide every incident edge; canonical dependency facts remain unchanged">Hide connections</button><button id="manual-unmute">Restore connections</button><button id="manual-reveal">Reveal temporarily</button><button id="manual-relax-group">Relax selected group</button></div><div id="manual-group-editor" hidden><h3>New group from selection</h3><label>Name <input id="manual-group-name" maxlength="80"></label><label>Color <input id="manual-group-color" type="color" value="#58a6ff"></label><label>Shape <select id="manual-group-shape"><option value="rectangle">Rectangle</option><option value="circle">Circle</option></select></label><button id="manual-group-create">Create</button><button id="manual-group-cancel">Cancel</button></div><hr><p id="manual-run-status">Paused</p><p id="manual-scope"></p><button id="manual-start-over">Start over from Explore…</button><p class="legend-help">Drag nodes within their group. Drag group headers to move regions; use the corner handle to resize. Ctrl/Cmd-click and box selection select multiple nodes.</p></div></aside><script id="graph-data" type="application/json">__DATA__</script><script src="cytoscape.min.js"></script><script src="d3-dispatch.min.js"></script><script src="d3-quadtree.min.js"></script><script src="d3-timer.min.js"></script><script src="d3-force.min.js"></script><script src="icons.js"></script><script src="manual-geometry.js"></script><script src="manual-state.js"></script><script src="manual-storage.js"></script><script src="manual-layout.js"></script><script src="manual-view.js"></script><script src="viewer.js"></script></body></html>""".Replace("__DATA__", payload, StringComparison.Ordinal);
        html = html.Replace("<link rel=\"stylesheet\" href=\"viewer.css\">", "<link rel=\"stylesheet\" href=\"coloris.min.css\"><link rel=\"stylesheet\" href=\"viewer.css\">", StringComparison.Ordinal);
        html = html.Replace("<script src=\"icons.js\"></script>", "<script src=\"coloris.min.js\"></script><script src=\"icons.js\"></script>", StringComparison.Ordinal);
        html = html.Replace("<aside id=\"manual-groups\">", "<aside id=\"manual-groups\"><input id=\"manual-search\" placeholder=\"Search nodes (3+ characters)…\" aria-label=\"Search Manual layout nodes\">", StringComparison.Ordinal);
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

    private static string Summary(DependencyGraph raw, DependencyGraph display, string viewerDirectory)
    {
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string DecimalNumber(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        var templatePath = Path.Combine(viewerDirectory, "summary-template.md");
        if (!File.Exists(templatePath)) throw new IOException($"Bundled summary template was not found at {templatePath}.");
        var template = File.ReadAllText(templatePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var components = display.Nodes.GroupBy(x => x.Component).OrderByDescending(x => x.Count()).ToArray();
        var analysis = raw.CommunityAnalysis ?? throw new InvalidDataException("Community analysis is required to generate the summary.");
        var selected = analysis.ResolutionProfile.SingleOrDefault(candidate => candidate.SelectedStandard);
        var nodesById = raw.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var standardCommunityKeys = analysis.GranularityAssignments["standard"].Values.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var standardCommunities = analysis.Communities
            .Where(community => standardCommunityKeys.Contains(community.StableKey))
            .OrderByDescending(community => community.Size)
            .ThenBy(community => community.Name, StringComparer.Ordinal)
            .ThenBy(community => community.StableKey, StringComparer.Ordinal)
            .ToArray();
        static string MarkdownCell(string value)
        {
            var escaped = SecurityElement.Escape(value) ?? "";
            return escaped.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COMPLETENESS_NOTICE"] = raw.Completeness.Complete ? "" : "> **Incomplete:** some projects could not be represented authoritatively. See `diagnostics.json`.\n\n",
            ["RAW_NODE_COUNT"] = Number(raw.Nodes.Count),
            ["RAW_EDGE_COUNT"] = Number(raw.Edges.Count),
            ["DISPLAY_NODE_COUNT"] = Number(display.Nodes.Count),
            ["DISPLAY_EDGE_COUNT"] = Number(display.Edges.Count),
            ["CONTRACTED_EDGE_COUNT"] = Number(display.Edges.Count(edge => edge.Derived)),
            ["DISCOVERED_PROJECT_COUNT"] = Number(raw.Completeness.DiscoveredProjects),
            ["EVALUATED_PROJECT_COUNT"] = Number(raw.Completeness.EvaluatedProjects),
            ["VALID_ASSETS_PROJECT_COUNT"] = Number(raw.Completeness.ProjectsWithValidAssets),
            ["COMPONENT_COUNT"] = Number(components.Count()),
            ["ISOLATED_NODE_COUNT"] = Number(display.Nodes.Count(node => node.InDegree == 0 && node.OutDegree == 0)),
            ["WARNING_COUNT"] = Number(raw.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)),
            ["ERROR_COUNT"] = Number(raw.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)),
            ["FRAMEWORKS"] = raw.TargetFrameworks.Count == 0 ? "none" : string.Join(", ", raw.TargetFrameworks),
            ["RIDS"] = raw.RuntimeIdentifiers.Count == 0 ? "none" : string.Join(", ", raw.RuntimeIdentifiers),
            ["STANDARD_RESOLUTION"] = selected is null ? "unknown" : DecimalNumber(selected.Resolution, "0.########"),
            ["STANDARD_COMMUNITY_COUNT"] = Number(analysis.GranularityAssignments["standard"].Values.Distinct(StringComparer.Ordinal).Count()),
            ["STANDARD_STABILITY"] = selected is null ? "unknown" : DecimalNumber(selected.Stability, "0.###"),
            ["COMMUNITY_SCOPE"] = analysis.Projection.Scope,
            ["DETECTION_VERTEX_COUNT"] = Number(analysis.Projection.DetectionVertexCount),
            ["DETECTION_NODE_COUNT"] = Number(analysis.Projection.DetectionNodeCount),
            ["LOCAL_PROJECT_COUNT"] = Number(analysis.Projection.LocalProjectCount),
            ["LOCAL_PRODUCED_PACKAGE_COUNT"] = Number(analysis.Projection.LocalProducedPackageCount),
            ["COLLAPSED_PRODUCER_PAIR_COUNT"] = Number(analysis.Projection.CollapsedProducerPairCount),
            ["EXCLUDED_TEST_PROJECT_COUNT"] = Number(analysis.Projection.ExcludedTestProjectCount),
            ["EXCLUDED_SYSTEM_PACKAGE_COUNT"] = Number(analysis.Projection.ExcludedSystemPackageCount),
            ["EXCLUDED_THIRD_PARTY_PACKAGE_COUNT"] = Number(analysis.Projection.ExcludedThirdPartyPackageCount),
            ["EXCLUDED_UNRESOLVED_EXTERNAL_COUNT"] = Number(analysis.Projection.ExcludedUnresolvedExternalCount),
            ["INCLUDED_UNMAPPED_INTERNAL_PACKAGE_COUNT"] = Number(analysis.Projection.IncludedUnmappedInternalPackageCount),
            ["INCLUDED_SYSTEM_PACKAGE_COUNT"] = Number(analysis.Projection.IncludedSystemPackageCount),
            ["INCLUDED_THIRD_PARTY_PACKAGE_COUNT"] = Number(analysis.Projection.IncludedThirdPartyPackageCount),
            ["DETECTION_CONTRACTED_EDGE_COUNT"] = Number(analysis.Projection.ContractedEdgeCount),
            ["CONTRACTED_PATH_WEIGHT"] = DecimalNumber(analysis.Settings.EdgeWeights.ContractedPath, "0.###"),
            ["PROJECT_REFERENCE_WEIGHT"] = DecimalNumber(analysis.Settings.EdgeWeights.ProjectReference, "0.###"),
            ["PACKAGE_REFERENCE_WEIGHT"] = DecimalNumber(analysis.Settings.EdgeWeights.PackageReference, "0.###"),
            ["PACKAGE_DEPENDENCY_WEIGHT"] = DecimalNumber(analysis.Settings.EdgeWeights.PackageDependency, "0.###"),
            ["TESTS_EXCLUDED"] = analysis.ProjectionRules.ExcludeTests.ToString(),
            ["PRODUCER_PAIRS_COLLAPSED"] = analysis.ProjectionRules.CollapseLocalProducerPackages.ToString(),
            ["COMMUNITY_ROWS"] = string.Join("\n", standardCommunities.Select(community => $"| {MarkdownCell(community.Name)} | `{community.StableKey}` | {Number(community.Size)} | {Number(community.DetectionVertexCount)} | {Number(community.ExpandedProducerPackageCount)} | {Number(community.ProjectCount)} | {Number(community.PackageCount)} | {Number(community.TestProjectCount)} | {Number(community.RunnableCount)} | {MarkdownCell(community.RepresentativeNodeIds.Count == 0 ? "none — no eligible source representative" : string.Join(", ", community.RepresentativeNodeIds.Select(id => nodesById.GetValueOrDefault(id)?.Label ?? id)))} |")),
            ["COMPONENT_ROWS"] = string.Join("\n", components.Select(component => $"- Component {component.Key}: {component.Count()} nodes; representative: {MarkdownCell(string.Join(", ", component.OrderByDescending(node => node.Centrality).ThenBy(node => node.Label).Take(3).Select(node => node.Label)))}"))
        };
        foreach (var value in values)
        {
            var marker = "{{" + value.Key + "}}";
            if (template.CountOccurrences(marker) != 1) throw new InvalidDataException($"Summary template must contain exactly one {marker} marker.");
            template = template.Replace(marker, value.Value, StringComparison.Ordinal);
        }
        if (template.Contains("{{", StringComparison.Ordinal)) throw new InvalidDataException("Summary template contains an unknown marker.");
        return template.TrimEnd() + "\n";
    }

    private static int CountOccurrences(this string value, string needle)
    {
        var count = 0; var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0) { count++; offset += needle.Length; }
        return count;
    }
}
