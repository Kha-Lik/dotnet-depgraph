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

    [Fact]
    public void ProjectFilterHidesProducerButRetainsItsPackage()
    {
        var project = new GraphNode { Id = "project:src/Generator/Generator.csproj", Label = "Generator", Kind = NodeKind.Project, Path = "src/Generator/Generator.csproj" };
        var package = new GraphNode { Id = "package:company.generated", Label = "Company.Generated", Kind = NodeKind.Package };
        var producer = new GraphEdge { Id = "producer", Source = project.Id, Target = package.Id, Kind = EdgeKind.ProducesPackage };
        var raw = GraphAnalysis.Analyze(new DependencyGraph { Root = "/repo", Nodes = [project, package], Edges = [producer] });

        var view = GraphFilter.Apply(raw, [], [], FilterMode.Strict, [], ["src/Generator/*"]);

        Assert.DoesNotContain(view.Nodes, x => x.Id == project.Id);
        Assert.Contains(view.Nodes, x => x.Id == package.Id);
        Assert.Empty(view.Edges);
    }

    [Fact]
    public void ContractModeBridgesDependenciesThroughHiddenProject()
    {
        var app = new GraphNode { Id = "project:src/App/App.csproj", Label = "App", Kind = NodeKind.Project, Path = "src/App/App.csproj" };
        var hidden = new GraphNode { Id = "project:src/Generator/Generator.csproj", Label = "Generator", Kind = NodeKind.Project, Path = "src/Generator/Generator.csproj" };
        var package = new GraphNode { Id = "package:company.runtime", Label = "Company.Runtime", Kind = NodeKind.Package };
        var edges = new[]
        {
            new GraphEdge { Id = "project", Source = app.Id, Target = hidden.Id, Kind = EdgeKind.ProjectReference },
            new GraphEdge { Id = "package", Source = hidden.Id, Target = package.Id, Kind = EdgeKind.PackageReference }
        };
        var raw = GraphAnalysis.Analyze(new DependencyGraph { Root = "/repo", Nodes = [app, hidden, package], Edges = edges });

        var edge = Assert.Single(GraphFilter.Apply(raw, [], [], FilterMode.Contract, [], ["src/Generator/*"]).Edges);

        Assert.Equal(EdgeKind.ContractedPath, edge.Kind);
        Assert.Equal(app.Id, edge.Source);
        Assert.Equal(package.Id, edge.Target);
        Assert.Equal([hidden.Id], edge.HiddenPathSamples.Single());
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
