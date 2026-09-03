using System.Text.Json;
using DotNetDependencyGraph.Core;
using Xunit;

namespace DotNetDependencyGraph.IntegrationTests;

public sealed class IntegrationTests
{
    [Fact]
    public void ScansRepositoryAssetsAndEvaluatedProjectReferences()
    {
        var root = RepositoryRoot(); var result = new Scanner().Scan(new() { Root = root, OutputDirectory = Path.Combine(root, "sample-output"), ExcludePaths = ["fixtures/*"], TargetFrameworks = ["all"] }, TestContext.Current.CancellationToken);
        Assert.True(result.Graph.Completeness.Complete); Assert.True(result.Projects.Count >= 4);
        Assert.Contains(result.Graph.Edges, x => x.Kind == EdgeKind.ProjectReference);
        Assert.Contains(result.Graph.Edges, x => x.Kind == EdgeKind.PackageReference);
        Assert.Contains(result.Graph.Edges, x => x.Kind == EdgeKind.PackageDependency);
        Assert.Contains(result.Graph.Nodes, x => x.Kind == NodeKind.Package && x.Label.Equals("NuGet.Common", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WritesOfflineReportWithEscapedEmbeddedDataAndControls()
    {
        var temp = Path.Combine(Path.GetTempPath(), "depgraph-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var graph = new DependencyGraph { Root = "/repo", Nodes = [new() { Id = "project:x", Label = "</script><script>alert(1)</script>", Kind = NodeKind.Project }], Edges = [], Completeness = new() { Complete = false } };
            OutputWriter.Write(temp, graph, new([], [], [], [], FilterMode.Contract, 42, true), Path.Combine(RepositoryRoot(), "src", "DotNetDependencyGraph.Cli", "viewer"));
            foreach (var file in new[] { "index.html", "graph.json", "diagnostics.json", "summary.md", "graph.graphml", "viewer.js", "viewer.css", "cytoscape.min.js", "d3-dispatch.min.js", "d3-quadtree.min.js", "d3-timer.min.js", "d3-force.min.js", "THIRD-PARTY-NOTICES.txt" }) Assert.True(File.Exists(Path.Combine(temp, file)), file);
            var html = File.ReadAllText(Path.Combine(temp, "index.html")); Assert.DoesNotContain("</script><script>alert(1)</script>", html); Assert.Contains("id=\"search\"", html); Assert.Contains("id=\"hops\"", html); Assert.Contains("id=\"component\"", html); Assert.Contains("id=\"repulsion\"", html); Assert.DoesNotContain("src=\"http", html, StringComparison.OrdinalIgnoreCase);
            var viewer = File.ReadAllText(Path.Combine(temp, "viewer.js")); Assert.Contains("forceCollide", viewer); Assert.Contains("search-match", viewer); Assert.Contains("\"text-wrap\": \"none\"", viewer); Assert.Contains("drag-threshold", viewer); Assert.Contains(PhysicsDefaults.StorageKey, html); Assert.Contains("id=\"important-label-count\"", html); Assert.Contains("dotnet-depgraph.viewer.v1", html);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp, "graph.json"))); Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task RenderCommandValidatesSchemaAndDoesNotRequireSourceTree()
    {
        var temp = Path.Combine(Path.GetTempPath(), "depgraph-render-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var graphPath = Path.Combine(temp, "input.json"); var output = Path.Combine(temp, "report");
            var hiddenProject = new GraphNode { Id = "project:hidden/Generator.csproj", Label = "Generator", Kind = NodeKind.Project, Path = "hidden/Generator.csproj" };
            var producedPackage = new GraphNode { Id = "package:generated", Label = "Generated", Kind = NodeKind.Package };
            var graph = new DependencyGraph { Root = "/path/that/does/not/exist", Nodes = [hiddenProject, producedPackage], Edges = [new() { Id = "producer", Source = hiddenProject.Id, Target = producedPackage.Id, Kind = EdgeKind.ProducesPackage }], Completeness = new() { Complete = true } };
            await File.WriteAllTextAsync(graphPath, JsonSerializer.Serialize(graph, OutputWriter.JsonOptions), TestContext.Current.CancellationToken);
            Assert.Equal(0, await ProgramEntry.RunAsync(["render", "--graph", graphPath, "--output", output, "--exclude-project", "hidden/*", "--collapse-local-packages"]));
            Assert.True(File.Exists(Path.Combine(output, "index.html")));
            var html = await File.ReadAllTextAsync(Path.Combine(output, "index.html"), TestContext.Current.CancellationToken);
            Assert.Contains("\"collapseLocalPackages\": true", html);
            var jsonStart = html.IndexOf(">", html.IndexOf("id=\"graph-data\"", StringComparison.Ordinal), StringComparison.Ordinal) + 1;
            var jsonEnd = html.IndexOf("</script>", jsonStart, StringComparison.Ordinal);
            using var payload = JsonDocument.Parse(html[jsonStart..jsonEnd]);
            var contractNodes = payload.RootElement.GetProperty("contract").GetProperty("nodes");
            Assert.DoesNotContain(contractNodes.EnumerateArray(), node => node.GetProperty("id").GetString() == hiddenProject.Id);
            Assert.Contains(contractNodes.EnumerateArray(), node => node.GetProperty("id").GetString() == producedPackage.Id);
            await File.WriteAllTextAsync(graphPath, "{\"schemaVersion\":\"9.0\",\"root\":\"/\",\"nodes\":[],\"edges\":[]}", TestContext.Current.CancellationToken);
            Assert.Equal(2, await ProgramEntry.RunAsync(["render", "--graph", graphPath, "--output", output, "--force"]));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void ExtractsMultiTargetAndRidContextsFromCommittedLockFile()
    {
        var root = RepositoryRoot(); var assets = Path.Combine(root, "fixtures", "LockFiles", "multitarget-rid.assets.json");
        var owner = new ProjectMetadata { FullPath = "/fixture/fixtures/Representative/Main/App.csproj", RelativePath = "Main/App.csproj", Id = "project:Main/App.csproj", Name = "App", AssemblyName = "App", PackageId = "App", AssetsFile = assets };
        var builder = new GraphBuilder("/fixture"); builder.AddNode(new() { Id = owner.Id, Label = "App", Kind = NodeKind.Project });
        Assert.True(new LockFileExtractor().Extract(owner, new Dictionary<string, ProjectMetadata>(), builder, ["all"], []));
        var graph = builder.Build(new() { Complete = true }); Assert.Contains("linux-x64", graph.RuntimeIdentifiers); Assert.Contains("win-x64", graph.RuntimeIdentifiers);
        Assert.Contains(graph.Edges, x => x.Source == "package:company.feature" && x.Target == "package:company.storage");
        Assert.Contains(graph.Edges, x => x.Source == "package:company.storage" && x.Target == "package:company.serialization");
        Assert.Contains(graph.Edges.SelectMany(x => x.Contexts), x => x.TargetFramework.StartsWith("net10.0-windows", StringComparison.Ordinal));
    }

    private static string RepositoryRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotNetDependencyGraph.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
}
