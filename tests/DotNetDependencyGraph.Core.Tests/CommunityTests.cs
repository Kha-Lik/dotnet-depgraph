using System.Text.Json;
using DotNetDependencyGraph.Core;
using Xunit;

namespace DotNetDependencyGraph.Core.Tests;

public sealed class LeidenCpmTests
{
    [Fact]
    public void CpmQualityMatchesHandCalculatedTriangle()
    {
        var ids = new[] { "a", "b", "c" };
        var edges = new[] { E("a", "b"), E("a", "c"), E("b", "c") };
        var together = ids.ToDictionary(id => id, _ => 0);
        Assert.Equal(1.5, LeidenCpm.Quality(ids, edges, together, .5), 8);
        var separate = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        Assert.Equal(0, LeidenCpm.Quality(ids, edges, separate, .5), 8);
    }

    [Fact]
    public void MoveDeltaEqualsFullQualityDifference()
    {
        var ids = new[] { "a", "b", "c" }; var edges = new[] { E("a", "b"), E("b", "c") };
        var membership = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["c"] = 1 };
        var before = LeidenCpm.Quality(ids, edges, membership, .25);
        var moved = new Dictionary<string, int>(membership) { ["b"] = 1 };
        var expected = LeidenCpm.Quality(ids, edges, moved, .25) - before;
        Assert.Equal(expected, LeidenCpm.MoveDelta(ids, edges, membership, "b", 1, .25), 8);
    }

    [Fact]
    public void FixedSeedIsDeterministicConnectedAndInputOrderIndependent()
    {
        var ids = Enumerable.Range(0, 12).Select(i => $"n{i:00}").ToArray();
        var edges = Enumerable.Range(0, 12).Select(i => E(ids[i], ids[(i + 1) % ids.Length])).ToArray();
        var first = LeidenCpm.Detect(ids, edges, .2, 42);
        var second = LeidenCpm.Detect(ids.Reverse().ToArray(), edges.Reverse().ToArray(), .2, 42);
        Assert.True(first.Connected); Assert.Equal(first.Quality, second.Quality, 8);
        Assert.Equal(first.Membership.OrderBy(x => x.Key), second.Membership.OrderBy(x => x.Key));
    }

    [Fact]
    public void CommunitiesNeverCrossDisconnectedComponents()
    {
        var result = LeidenCpm.Detect(["a", "b", "x", "y"], [E("a", "b"), E("x", "y")], .1, 3);
        Assert.NotEqual(result.Membership["a"], result.Membership["x"]); Assert.True(result.Connected);
    }

    [Fact]
    public void MatchesTrustedReferencePartitionForTwoCliques()
    {
        // leidenalg 0.12.0 / igraph 1.0.0, CPM gamma=.2, seed=42 gives
        // [0,0,0,1,1,1] and quality 9.6 (its objective is exactly 2x ours).
        var ids = new[] { "a", "b", "c", "d", "e", "f" };
        var edges = new[] { E("a", "b"), E("a", "c"), E("b", "c"), E("d", "e"), E("d", "f"), E("e", "f"), E("c", "d", .2) };
        var result = LeidenCpm.Detect(ids, edges, .2, 42);
        Assert.Equal(result.Membership["a"], result.Membership["b"]); Assert.Equal(result.Membership["a"], result.Membership["c"]);
        Assert.Equal(result.Membership["d"], result.Membership["e"]); Assert.Equal(result.Membership["d"], result.Membership["f"]);
        Assert.NotEqual(result.Membership["a"], result.Membership["d"]); Assert.Equal(4.8, result.Quality, 8);
    }

    private static CommunityProjectionEdge E(string source, string target, double weight = 1) => new(source, target, weight);
}

public sealed class CommunityProjectionTests
{
    [Fact]
    public void ProducerPairCollapsesAndContextsDoNotInflateWeight()
    {
        var project = N("project:feature", NodeKind.Project); var package = N("package:feature", NodeKind.Package); var other = N("project:other", NodeKind.Project);
        var graph = new DependencyGraph
        {
            Root = "/",
            Nodes = [project, package, other],
            Edges =
        [
            new() { Id = "producer", Source = project.Id, Target = package.Id, Kind = EdgeKind.ProducesPackage },
            new() { Id = "ref", Source = other.Id, Target = package.Id, Kind = EdgeKind.PackageReference, Contexts = [new("o", "a", "net10.0", null, null, null, true, 99)] }
        ]
        };
        var projection = CommunityProjectionBuilder.Build(graph, new());
        Assert.Equal(2, projection.Vertices.Count); Assert.Equal(projection.OriginalToVertex[project.Id], projection.OriginalToVertex[package.Id]);
        Assert.Equal(2, Assert.Single(projection.Edges).Weight);
    }

    [Fact]
    public void TestsAndContractedPathsAreExcluded()
    {
        var test = N("test", NodeKind.Project) with { Classification = "test-project" }; var production = N("prod", NodeKind.Project);
        var graph = new DependencyGraph { Root = "/", Nodes = [test, production], Edges = [new() { Id = "derived", Source = test.Id, Target = production.Id, Kind = EdgeKind.ContractedPath, Derived = true }] };
        var projection = CommunityProjectionBuilder.Build(graph, new());
        Assert.Single(projection.Vertices); Assert.Empty(projection.Edges); Assert.Equal([test.Id], projection.ExcludedTestNodeIds);
    }

    private static GraphNode N(string id, NodeKind kind) => new() { Id = id, Label = id, Kind = kind };
}

public sealed class CommunityAnalysisTests
{
    [Fact]
    public void NestedFixtureSplitsOnlyAtSupportedFineLevel()
    {
        var nodes = Enumerable.Range(0, 12).Select(i => new GraphNode { Id = $"project:Example.{(i < 6 ? "Identity" : "Maintenance")}.{i}", Label = $"Example.{(i < 6 ? "Identity" : "Maintenance")}.{i}", Kind = NodeKind.Project, Classification = "class-library" }).ToArray();
        var edges = new List<GraphEdge>();
        void Add(int a, int b, double marker = 0) => edges.Add(new() { Id = $"e:{a}:{b}:{marker}", Source = nodes[a].Id, Target = nodes[b].Id, Kind = EdgeKind.ProjectReference });
        for (var start = 0; start < 12; start += 3) for (var i = start; i < start + 3; i++) for (var j = i + 1; j < start + 3; j++) Add(i, j);
        foreach (var (a, b) in new[] { (0, 3), (1, 4), (6, 9), (7, 10), (2, 6), (5, 8), (1, 7) }) Add(a, b);
        var settings = new CommunitySettings { Resolution = .2, Levels = 3, Trials = 3, MinSize = 2 };
        var graph = GraphAnalysis.Analyze(new() { Root = "/fictional", Nodes = nodes, Edges = edges }, communitySettings: settings);
        var analysis = Assert.IsType<CommunityAnalysis>(graph.CommunityAnalysis);
        var coarse = analysis.GranularityAssignments["coarse"].Values.Distinct().Count();
        var standard = analysis.GranularityAssignments["standard"].Values.Distinct().Count();
        var fine = analysis.GranularityAssignments["fine"].Values.Distinct().Count();
        Assert.True(coarse <= standard); Assert.True(standard <= fine); Assert.True(fine > coarse);
        Assert.All(analysis.Communities, community => Assert.NotEmpty(community.MemberNodeIds));
        Assert.All(analysis.Communities, community =>
        {
            Assert.Matches("^#[0-9A-F]{6}$", community.Color);
            Assert.Matches("^#[0-9A-F]{6}$", community.BorderColor);
        });
        Assert.Equal(analysis.Communities.Count, analysis.Communities.Select(community => community.Color + "/" + community.BorderColor).Distinct().Count());
        Assert.All(analysis.NodeAssignments.Values, assignment => Assert.NotEmpty(assignment.DetectedCommunityPath));
    }

    [Fact]
    public void TestAssignmentAndRunnableBlastRadiusAreDirectionallyCorrect()
    {
        var app = new GraphNode { Id = "app", Label = "Example.App", Kind = NodeKind.Project, Classification = "executable" };
        var feature = new GraphNode { Id = "feature", Label = "Example.Identity", Kind = NodeKind.Project, Classification = "class-library" };
        var test = new GraphNode { Id = "test", Label = "Example.Identity.Tests", Kind = NodeKind.Project, Classification = "test-project" };
        var graph = GraphAnalysis.Analyze(new DependencyGraph
        {
            Root = "/fictional",
            Nodes = [app, feature, test],
            Edges =
        [
            new() { Id = "app-feature", Source = app.Id, Target = feature.Id, Kind = EdgeKind.ProjectReference },
            new() { Id = "test-feature", Source = test.Id, Target = feature.Id, Kind = EdgeKind.ProjectReference }
        ]
        }, communitySettings: new() { Trials = 2 });
        Assert.Equal("inherited-test", graph.CommunityAnalysis!.NodeAssignments[test.Id].AssignmentSource);
        Assert.Equal([app.Id], graph.Nodes.Single(node => node.Id == feature.Id).RunnableDependentIds);
        Assert.Contains(graph.CommunityAnalysis.RunnableImpactPaths, path => path.Source == app.Id && path.Target == feature.Id);
    }

    [Fact]
    public void CyclicBlastPathsAndDirectedCommunityCycleRemainExact()
    {
        var nodes = new[]
        {
            new GraphNode { Id = "app", Label = "Example.App", Kind = NodeKind.Project, Classification = "executable" },
            new GraphNode { Id = "a", Label = "Example.A", Kind = NodeKind.Project, Classification = "class-library" },
            new GraphNode { Id = "b", Label = "Example.B", Kind = NodeKind.Project, Classification = "class-library" },
            new GraphNode { Id = "target", Label = "Example.Target", Kind = NodeKind.Project, Classification = "class-library" }
        };
        var edges = new[]
        {
            new GraphEdge { Id = "app-a", Source = "app", Target = "a", Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "app-b", Source = "app", Target = "b", Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "a-b", Source = "a", Target = "b", Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "b-a", Source = "b", Target = "a", Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "a-target", Source = "a", Target = "target", Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "b-target", Source = "b", Target = "target", Kind = EdgeKind.ProjectReference }
        };
        var graph = GraphAnalysis.Analyze(new() { Root = "/fictional", Nodes = nodes, Edges = edges },
            communitySettings: new() { Resolution = 20, Trials = 2, TargetSize = 1, MinSize = 1 });
        Assert.Equal(["app"], graph.Nodes.Single(node => node.Id == "a").RunnableDependentIds);
        Assert.Equal(["app"], graph.Nodes.Single(node => node.Id == "b").RunnableDependentIds);
        Assert.Equal(["app"], graph.Nodes.Single(node => node.Id == "target").RunnableDependentIds);
        var targetPaths = graph.CommunityAnalysis!.RunnableImpactPaths.Where(path => path.Source == "app" && path.Target == "target").ToArray();
        Assert.Equal(2, targetPaths.Length);
        Assert.All(targetPaths, path => Assert.Equal(2, path.EdgeIds.Count));
        var standard = graph.CommunityAnalysis.GranularityAssignments["standard"];
        Assert.NotEqual(standard["a"], standard["b"]);
        Assert.Contains(graph.CommunityAnalysis.CrossCommunityDependencies, row => row.SourceCommunity == standard["a"] && row.TargetCommunity == standard["b"]);
        Assert.Contains(graph.CommunityAnalysis.CrossCommunityDependencies, row => row.SourceCommunity == standard["b"] && row.TargetCommunity == standard["a"]);
        Assert.Contains(graph.CommunityAnalysis.CommunityCycles, cycle => cycle.CommunityKeys.Contains(standard["a"]) && cycle.CommunityKeys.Contains(standard["b"]));
    }

    [Fact]
    public void CommunityAnalysisSerializationIsByteDeterministic()
    {
        var nodes = new[] { "a", "b", "c" }.Select(id => new GraphNode { Id = id, Label = id, Kind = NodeKind.Package }).ToArray();
        var edges = new[] { new GraphEdge { Id = "ab", Source = "a", Target = "b", Kind = EdgeKind.PackageDependency }, new GraphEdge { Id = "bc", Source = "b", Target = "c", Kind = EdgeKind.PackageDependency } };
        var settings = new CommunitySettings { Trials = 3, Seed = 19 };
        string Run() => JsonSerializer.Serialize(GraphAnalysis.Analyze(new() { Root = "/", Nodes = nodes, Edges = edges }, communitySettings: settings).CommunityAnalysis, OutputWriter.JsonOptions);
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void CommunityAnalysisReportsBoundedProgressStages()
    {
        var messages = new List<string>();
        CommunityAnalyzer.Analyze(new DependencyGraph
        {
            Root = "/fictional",
            Nodes = [new() { Id = "a", Label = "A", Kind = NodeKind.Package }, new() { Id = "b", Label = "B", Kind = NodeKind.Package }],
            Edges = [new() { Id = "ab", Source = "a", Target = "b", Kind = EdgeKind.PackageDependency }]
        }, new() { Trials = 1, Levels = 1 }, messages.Add);
        Assert.Contains(messages, message => message.StartsWith("Community projection:", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.StartsWith("Evaluating CPM resolution", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.StartsWith("Strict hierarchy built:", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.StartsWith("Architecture analysis complete:", StringComparison.Ordinal));
    }

    [Fact]
    public void SchemaValidationDetectsTamperingAndMissingAssignments()
    {
        var graph = GraphAnalysis.Analyze(new DependencyGraph { Root = "/", Nodes = [new() { Id = "a", Label = "A", Kind = NodeKind.Package }], Edges = [] });
        GraphSchema.Validate(graph);
        Assert.Throws<ArgumentException>(() => GraphSchema.Validate(graph with { Nodes = graph.Nodes.Append(new GraphNode { Id = "b", Label = "B", Kind = NodeKind.Package }).ToArray() }));
    }
}
