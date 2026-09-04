using System.Text.Json;
using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Application.Filtering;
using DotNetDependencyGraph.Core.Application.Scanning;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.MSBuild;
using DotNetDependencyGraph.Core.Infrastructure.NuGet;
using DotNetDependencyGraph.Core.Infrastructure.Output;
using DotNetDependencyGraph.Core.Reporting.Rendering;
using Xunit;

namespace DotNetDependencyGraph.Core.Tests;

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
