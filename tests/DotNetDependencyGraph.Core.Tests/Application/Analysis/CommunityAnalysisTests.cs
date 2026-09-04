using System.Text.Json;
using DotNetDependencyGraph.Core.Algorithms.Communities;
using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.Output;
using Xunit;

namespace DotNetDependencyGraph.Core.Tests;

public sealed class CommunityAnalysisTests
{
    [Fact]
    public void NestedFixtureSplitsOnlyAtSupportedFineLevel()
    {
        var nodes = Enumerable.Range(0, 12).Select(i => new GraphNode { Id = $"project:Example.{(i < 6 ? "Identity" : "Maintenance")}.{i}", Label = $"Example.{(i < 6 ? "Identity" : "Maintenance")}.{i}", Kind = NodeKind.Project, Path = $"Example.{i}.csproj", Classification = "class-library" }).ToArray();
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
        var app = new GraphNode { Id = "app", Label = "Example.App", Kind = NodeKind.Project, Path = "App.csproj", Classification = "executable" };
        var feature = new GraphNode { Id = "feature", Label = "Example.Identity", Kind = NodeKind.Project, Path = "Identity.csproj", Classification = "class-library" };
        var test = new GraphNode { Id = "test", Label = "Example.Identity.Tests", Kind = NodeKind.Project, Path = "Identity.Tests.csproj", Classification = "test-project" };
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
            new GraphNode { Id = "app", Label = "Example.App", Kind = NodeKind.Project, Path = "App.csproj", Classification = "executable" },
            new GraphNode { Id = "a", Label = "Example.A", Kind = NodeKind.Project, Path = "A.csproj", Classification = "class-library" },
            new GraphNode { Id = "b", Label = "Example.B", Kind = NodeKind.Project, Path = "B.csproj", Classification = "class-library" },
            new GraphNode { Id = "target", Label = "Example.Target", Kind = NodeKind.Project, Path = "Target.csproj", Classification = "class-library" }
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
    public void ExternalWebPackagesCannotJoinNameOrRepresentSourceCommunityByDefault()
    {
        var web = new GraphNode { Id = "project:web", Label = "Acme.User.Web", Kind = NodeKind.Project, Path = "src/User/Web.csproj", Classification = "web-application" };
        var swagger = new GraphNode { Id = "package:swashbuckle.aspnetcore", Label = "Swashbuckle.AspNetCore", Kind = NodeKind.Package };
        var framework = new GraphNode { Id = "package:microsoft.aspnetcore.mvc", Label = "Microsoft.AspNetCore.Mvc", Kind = NodeKind.Package };
        var rawEdges = new[]
        {
            new GraphEdge { Id = "web-swagger", Source = web.Id, Target = swagger.Id, Kind = EdgeKind.PackageReference },
            new GraphEdge { Id = "swagger-framework", Source = swagger.Id, Target = framework.Id, Kind = EdgeKind.PackageDependency }
        };
        var graph = GraphAnalysis.Analyze(new() { Root = "/repo", Nodes = [web, swagger, framework], Edges = rawEdges });
        var analysis = graph.CommunityAnalysis!; var community = Assert.Single(analysis.Communities);
        Assert.Equal([web.Id], community.MemberNodeIds);
        Assert.Equal([web.Id], community.RepresentativeNodeIds);
        Assert.DoesNotContain(community.NameEvidence.SelectMany(evidence => evidence.Members), id => id == swagger.Id || id == framework.Id);
        Assert.Empty(analysis.NodeAssignments[swagger.Id].DetectedCommunityPath);
        Assert.Empty(analysis.NodeAssignments[framework.Id].DetectedCommunityPath);
        Assert.Equal(CommunityNodeOwnership.ThirdPartyPackage, analysis.NodeOwnership[swagger.Id]);
        Assert.Equal(CommunityNodeOwnership.SystemPackage, analysis.NodeOwnership[framework.Id]);
        Assert.Equal(new[] { web.Id, swagger.Id, framework.Id }.Order(StringComparer.Ordinal), graph.Nodes.Select(node => node.Id));
        Assert.Equal(rawEdges.Select(edge => edge.Id), graph.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void ExternalOnlyPackagesCannotCreateDefaultCommunities()
    {
        var left = new GraphNode { Id = "package:serilog", Label = "Serilog", Kind = NodeKind.Package };
        var right = new GraphNode { Id = "package:skiasharp", Label = "SkiaSharp", Kind = NodeKind.Package };
        var graph = GraphAnalysis.Analyze(new() { Root = "/repo", Nodes = [left, right], Edges = [new() { Id = "external", Source = left.Id, Target = right.Id, Kind = EdgeKind.PackageDependency }] });
        Assert.Empty(graph.CommunityAnalysis!.Communities);
        Assert.Empty(graph.CommunityAnalysis.GranularityAssignments["standard"]);
        Assert.All(graph.CommunityAnalysis.NodeAssignments.Values, assignment => Assert.Empty(assignment.DetectedCommunityPath));
    }

    [Fact]
    public void ProducerExpansionHasDistinctCountsAndDeduplicatedRepresentatives()
    {
        var project = new GraphNode { Id = "project:contracts", Label = "Acme.Contracts", Kind = NodeKind.Project, Path = "src/Contracts.csproj", Classification = "packable-library" };
        var package = new GraphNode { Id = "package:acme.contracts", Label = "Acme.Contracts", Kind = NodeKind.Package };
        var graph = GraphAnalysis.Analyze(new()
        {
            Root = "/repo",
            Nodes = [project, package],
            Edges = [new() { Id = "producer", Source = project.Id, Target = package.Id, Kind = EdgeKind.ProducesPackage }]
        });
        var analysis = graph.CommunityAnalysis!; var community = Assert.Single(analysis.Communities);
        Assert.Equal(1, analysis.Projection.DetectionVertexCount);
        Assert.Equal(1, analysis.Projection.CollapsedProducerPairCount);
        Assert.Equal(2, community.Size); Assert.Equal(1, community.DetectionVertexCount); Assert.Equal(1, community.ExpandedProducerPackageCount);
        Assert.Equal([project.Id], community.RepresentativeNodeIds);
        Assert.Single(community.RepresentativeNodeIds.Select(id => graph.Nodes.Single(node => node.Id == id).Label).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void OptedInExternalOnlyCommunityReportsNoEligibleRepresentative()
    {
        var package = new GraphNode { Id = "package:third.party", Label = "Third.Party", Kind = NodeKind.Package };
        var graph = GraphAnalysis.Analyze(new() { Root = "/repo", Nodes = [package], Edges = [] }, communitySettings: new() { IncludeThirdPartyPackages = true });
        var community = Assert.Single(graph.CommunityAnalysis!.Communities);
        Assert.Empty(community.RepresentativeNodeIds);
        Assert.Equal("no-eligible-source-representative", community.RepresentativeStatus);
        Assert.Equal("No eligible source representative", community.Name);
        Assert.Contains(graph.CommunityAnalysis.Diagnostics, diagnostic => diagnostic.Code == "community-no-eligible-representative");
    }

    [Fact]
    public void SummaryProjectionCountsMatchActualDetectionInputs()
    {
        var source = new GraphNode { Id = "project:source", Label = "Source", Kind = NodeKind.Project, Path = "Source.csproj" };
        var test = new GraphNode { Id = "project:test", Label = "Source.Tests", Kind = NodeKind.Project, Path = "Source.Tests.csproj", Classification = "test-project" };
        var package = new GraphNode { Id = "package:external", Label = "External", Kind = NodeKind.Package };
        var graph = GraphAnalysis.Analyze(new() { Root = "/repo", Nodes = [source, test, package], Edges = [new() { Id = "test-source", Source = test.Id, Target = source.Id, Kind = EdgeKind.ProjectReference }] });
        var analysis = graph.CommunityAnalysis!;
        Assert.Equal(analysis.Projection.DetectionVertexCount, analysis.ResolutionProfile.Single(candidate => candidate.SelectedStandard).Sizes.Sum());
        Assert.Equal(analysis.Projection.DetectionNodeCount - analysis.Projection.CollapsedProducerPairCount, analysis.Projection.DetectionVertexCount);
        Assert.Equal(1, analysis.Projection.ExcludedTestProjectCount); Assert.Equal(1, analysis.Projection.ExcludedThirdPartyPackageCount);
        Assert.Equal(graph.Nodes.Count, analysis.NodeOwnership.Count);
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
        }, new() { Trials = 1, Levels = 1, IncludeThirdPartyPackages = true }, messages.Add);
        Assert.Contains(messages, message => message.StartsWith("Community projection (", StringComparison.Ordinal));
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
