using System.Text.Json;
using DotNetDependencyGraph.Core.Algorithms.Communities;
using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.Output;
using Xunit;

namespace DotNetDependencyGraph.Core.Tests;

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
