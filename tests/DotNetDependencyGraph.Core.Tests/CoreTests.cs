using System.Text.Json;
using DotNetDependencyGraph.Core;
using Xunit;

namespace DotNetDependencyGraph.Core.Tests;

public sealed class GlobTests
{
    [Theory]
    [InlineData("Company.Core", "Company.*", true)]
    [InlineData("company.Core", "COMPANY.*", true)]
    [InlineData("Another.Internal.XCore2", "Another.Internal.?Core*", true)]
    [InlineData("Company/Core", "Company/*", true)]
    [InlineData("Company.Core", "Company.?ore", true)]
    [InlineData("External.Core", "Company.*", false)]
    public void MatchesExpected(string value, string pattern, bool expected) => Assert.Equal(expected, Glob.IsMatch(value, pattern));

    [Fact] public void NormalizesWindowsAndUnixSeparators() => Assert.Equal("src/App/App.csproj", ProjectDiscovery.Normalize(@"./src\App/App.csproj"));
}

public sealed class FilterTests
{
    [Fact]
    public void StrictDoesNotInventBridge()
    {
        var raw = Graph(("Company.A", "External.Adapter"), ("External.Adapter", "Company.B"));
        var view = GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Strict);
        Assert.Equal(2, view.Nodes.Count); Assert.Empty(view.Edges);
    }

    [Fact]
    public void ContractAddsOneDescribedBridge()
    {
        var raw = Graph(("Company.A", "External.Adapter"), ("External.Adapter", "Company.B"));
        var edge = Assert.Single(GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Contract).Edges);
        Assert.Equal(EdgeKind.ContractedPath, edge.Kind); Assert.True(edge.Derived); Assert.Equal(1, edge.MinimumHiddenHops);
        Assert.Equal(["package:external.adapter"], edge.HiddenPathSamples.Single());
    }

    [Fact]
    public void DiamondCountsTwoPaths()
    {
        var raw = Graph(("Company.A", "X"), ("Company.A", "Y"), ("X", "Z"), ("Y", "Z"), ("Z", "Company.B"));
        var edge = Assert.Single(GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Contract).Edges);
        Assert.Equal(2, edge.PathCount); Assert.Equal(2, edge.MinimumHiddenHops);
    }

    [Fact]
    public void HiddenCycleTerminatesAndContracts()
    {
        var raw = Graph(("Company.A", "X"), ("X", "Y"), ("Y", "X"), ("Y", "Company.B"));
        var edge = Assert.Single(GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Contract).Edges);
        Assert.Equal("package:company.b", edge.Target);
    }

    [Fact]
    public void DoesNotSkipIntermediateRetainedNode()
    {
        var raw = Graph(("Company.A", "X"), ("X", "Company.Middle"), ("Company.Middle", "Y"), ("Y", "Company.B"));
        var edges = GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Contract).Edges;
        Assert.Contains(edges, x => x.Source == "package:company.a" && x.Target == "package:company.middle");
        Assert.Contains(edges, x => x.Source == "package:company.middle" && x.Target == "package:company.b");
        Assert.DoesNotContain(edges, x => x.Source == "package:company.a" && x.Target == "package:company.b");
    }

    [Fact]
    public void MultipleFirstRetainedTargetsAreKept()
    {
        var raw = Graph(("Company.A", "X"), ("X", "Company.B"), ("X", "Company.C"));
        var edges = GraphFilter.Apply(raw, ["Company.*"], [], FilterMode.Contract).Edges;
        Assert.Equal(2, edges.Count); Assert.Equal(2, edges.Select(x => x.Target).Distinct().Count());
    }

    private static DependencyGraph Graph(params (string Source, string Target)[] edges)
    {
        var names = edges.SelectMany(x => new[] { x.Source, x.Target }).Distinct().ToArray();
        var nodes = names.Select(n => new GraphNode { Id = "package:" + n.ToLowerInvariant(), Label = n, Kind = NodeKind.Package }).ToArray();
        var context = new EdgeContext("project:test", "obj/project.assets.json", "net10.0", null, null, "1.0.0", false);
        var graphEdges = edges.Select((e, i) => new GraphEdge { Id = "e" + i, Source = "package:" + e.Source.ToLowerInvariant(), Target = "package:" + e.Target.ToLowerInvariant(), Kind = EdgeKind.PackageDependency, Contexts = [context] }).ToArray();
        return GraphAnalysis.Analyze(new DependencyGraph { Root = "/repo", Nodes = nodes, Edges = graphEdges, Completeness = new() { Complete = true } });
    }
}

public sealed class GraphTests
{
    [Fact]
    public void BuilderAggregatesVersionsAndContextsDeterministically()
    {
        var b = new GraphBuilder("/repo"); b.AddNode(new() { Id = "package:a", Label = "A", Kind = NodeKind.Package, Versions = ["2.0.0"] }); b.AddNode(new() { Id = "package:a", Label = "A", Kind = NodeKind.Package, Versions = ["1.0.0"] });
        var c = new EdgeContext("project:p", "a", "net10.0", null, "[1,)", "2.0.0", true); b.AddNode(new() { Id = "project:p", Label = "P", Kind = NodeKind.Project }); b.AddEdge("project:p", "package:a", EdgeKind.PackageReference, c); b.AddEdge("project:p", "package:a", EdgeKind.PackageReference, c);
        var g = b.Build(new() { Complete = true }); var n = g.Nodes.Single(x => x.Id == "package:a"); Assert.Equal(["1.0.0", "2.0.0"], n.Versions); Assert.True(n.VersionSkew); Assert.Equal(2, g.Edges.Single().Contexts.Single().ObservationCount);
    }

    [Fact]
    public void AnalysisFindsComponentsCyclesAndReachability()
    {
        var nodes = new[] { "a", "b", "c", "isolated" }.Select(x => new GraphNode { Id = x, Label = x, Kind = NodeKind.Package }).ToArray();
        var edges = new[] { ("a", "b"), ("b", "c"), ("c", "a") }.Select((x, i) => new GraphEdge { Id = i.ToString(), Source = x.Item1, Target = x.Item2, Kind = EdgeKind.PackageDependency }).ToArray();
        var g = GraphAnalysis.Analyze(new() { Root = "/", Nodes = nodes, Edges = edges }); Assert.Equal(2, g.Nodes.Select(x => x.Component).Distinct().Count()); Assert.All(g.Nodes.Where(x => x.Id != "isolated"), x => Assert.True(x.InCycle)); Assert.Equal(2, g.Nodes.Single(x => x.Id == "a").TransitiveDependencies);
    }

    [Fact]
    public void ProducerEdgeConnectsWeakComponentButNotDependencyMetrics()
    {
        var nodes = new[] { new GraphNode { Id = "p", Label = "P", Kind = NodeKind.Project }, new GraphNode { Id = "package:p", Label = "P", Kind = NodeKind.Package } };
        var g = GraphAnalysis.Analyze(new() { Root = "/", Nodes = nodes, Edges = [new() { Id = "e", Source = "p", Target = "package:p", Kind = EdgeKind.ProducesPackage }] });
        Assert.Single(g.Nodes.Select(x => x.Component).Distinct()); Assert.All(g.Nodes, x => Assert.Equal(0, x.OutDegree + x.InDegree));
    }

    [Fact]
    public void GraphMlEscapesUntrustedLabels()
    {
        var g = new DependencyGraph { Root = "/", Nodes = [new() { Id = "project:<x>", Label = "<script>alert(1)</script>", Kind = NodeKind.Project }], Edges = [] };
        var xml = OutputWriter.GraphMl(g); Assert.DoesNotContain("<script>", xml); Assert.Contains("&lt;script&gt;", xml);
    }

    [Fact]
    public void SerializationIsDeterministic()
    {
        var g = new DependencyGraph { Root = "/", Nodes = [new() { Id = "a", Label = "A", Kind = NodeKind.Package }], Edges = [] };
        Assert.Equal(JsonSerializer.Serialize(g, OutputWriter.JsonOptions), JsonSerializer.Serialize(g, OutputWriter.JsonOptions));
    }

    [Fact]
    public void AnalyzesTwoThousandNodesAndTenThousandEdges()
    {
        const int count = 2000; var nodes = Enumerable.Range(0, count).Select(i => new GraphNode { Id = $"n{i}", Label = $"N{i}", Kind = NodeKind.Package }).ToArray();
        var edges = Enumerable.Range(0, 10000).Select(i => new GraphEdge { Id = $"e{i}", Source = $"n{i % count}", Target = $"n{(i * 17 + 1) % count}", Kind = EdgeKind.PackageDependency }).ToArray();
        var graph = GraphAnalysis.Analyze(new() { Root = "/synthetic", Nodes = nodes, Edges = edges }); Assert.Equal(count, graph.Nodes.Count); Assert.Equal(10000, graph.Edges.Count);
    }
}

public sealed class RenderingPolicyTests
{
    [Fact]
    public void NodeSizeIsMonotonicBoundedAndDeterministic()
    {
        var values = new[] { 0, 1, 10, 100, 10_000 }.Select(value => RenderingPolicy.NodeDiameter(Node(value))).ToArray();
        Assert.Equal(values.Order().ToArray(), values);
        Assert.All(values, value => Assert.InRange(value, RenderingPolicy.MinimumNodeDiameter, RenderingPolicy.MaximumNodeDiameter));
        Assert.Equal(values, new[] { 0, 1, 10, 100, 10_000 }.Select(value => RenderingPolicy.NodeDiameter(Node(value))).ToArray());
    }

    [Fact]
    public void CollisionRadiusIncludesRenderedRadiusAndPadding()
    {
        var node = Node(12);
        Assert.Equal(RenderingPolicy.NodeDiameter(node) / 2 + 14, RenderingPolicy.CollisionRadius(node, 14));
    }

    [Fact]
    public void OverviewLabelsAreLimitedButInteractionZoomCanRevealThem()
    {
        var nodes = Enumerable.Range(0, 100).Select(i => Node(i + 1) with { Centrality = 1 }).ToArray();
        Assert.Empty(nodes.Where((node, rank) => RenderingPolicy.ShowOverviewLabel(node, rank, nodes.Length, .2)));
        Assert.True(nodes.Where((node, rank) => RenderingPolicy.ShowOverviewLabel(node, rank, nodes.Length, .8)).Count() < nodes.Length);
        Assert.All(nodes, (node) => Assert.True(RenderingPolicy.ShowOverviewLabel(node, 0, nodes.Length, 1.3)));
    }

    [Fact]
    public void PhysicsDefaultsAreBoundedAndStorageKeyIsVersioned()
    {
        Assert.True(new PhysicsDefaults().IsValid());
        Assert.EndsWith(".v2", PhysicsDefaults.StorageKey, StringComparison.Ordinal);
    }

    private static GraphNode Node(int importance) => new() { Id = $"n{importance}", Label = "N", Kind = NodeKind.Package, TransitiveDependents = importance };
}

public sealed class DiscoveryTests
{
    [Fact]
    public void FindsSupportedProjectsAndSkipsGeneratedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "depgraph-discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { Directory.CreateDirectory(Path.Combine(root, "src")); Directory.CreateDirectory(Path.Combine(root, "obj")); File.WriteAllText(Path.Combine(root, "src", "A.csproj"), ""); File.WriteAllText(Path.Combine(root, "src", "B.fsproj"), ""); File.WriteAllText(Path.Combine(root, "obj", "Hidden.csproj"), ""); var result = new ProjectDiscovery().Discover(root, cancellationToken: TestContext.Current.CancellationToken); Assert.Equal(2, result.Projects.Count); Assert.Empty(result.Diagnostics); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IncludeAndExcludeGlobsCompose()
    {
        var root = Path.Combine(Path.GetTempPath(), "depgraph-glob-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "src"));
        try { File.WriteAllText(Path.Combine(root, "src", "A.csproj"), ""); File.WriteAllText(Path.Combine(root, "src", "A.Tests.csproj"), ""); var result = new ProjectDiscovery().Discover(root, ["src/*"], ["*.Tests.csproj", "src/*.Tests.csproj"], cancellationToken: TestContext.Current.CancellationToken); Assert.Single(result.Projects); } finally { Directory.Delete(root, true); }
    }
}

public sealed class ExtractionDiagnosticsTests
{
    [Fact]
    public void RejectsMalformedAssetsThatNuGetParsesAsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "depgraph-malformed-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ invalid");
        try
        {
            var owner = new ProjectMetadata { FullPath = "/repo/App.csproj", RelativePath = "App.csproj", Id = "project:App.csproj", Name = "App", AssemblyName = "App", PackageId = "App", AssetsFile = path };
            var builder = new GraphBuilder("/repo");
            Assert.False(new LockFileExtractor().Extract(owner, new Dictionary<string, ProjectMetadata>(), builder, ["all"], []));
            Assert.Equal(1, builder.DiagnosticCount("assets-malformed"));
        }
        finally { File.Delete(path); }
    }
}
