using System.Text.Json;
using DotNetDependencyGraph.Core.Algorithms.Communities;
using DotNetDependencyGraph.Core.Application.Analysis;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.Output;
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
