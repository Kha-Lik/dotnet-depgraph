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
        Assert.InRange(new PhysicsDefaults().DragThreshold, 0, 30);
        Assert.EndsWith(".v3", PhysicsDefaults.StorageKey, StringComparison.Ordinal);
    }

    private static GraphNode Node(int importance) => new() { Id = $"n{importance}", Label = "N", Kind = NodeKind.Package, TransitiveDependents = importance };
}
