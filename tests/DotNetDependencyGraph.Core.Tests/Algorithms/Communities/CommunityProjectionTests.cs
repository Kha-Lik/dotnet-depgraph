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
            Root = "/repo",
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
        var graph = new DependencyGraph { Root = "/repo", Nodes = [test, production], Edges = [new() { Id = "derived", Source = test.Id, Target = production.Id, Kind = EdgeKind.ContractedPath, Derived = true }] };
        var projection = CommunityProjectionBuilder.Build(graph, new());
        Assert.Single(projection.Vertices); Assert.Empty(projection.Edges); Assert.Equal([test.Id], projection.ExcludedTestNodeIds);
    }

    [Fact]
    public void ExternalOnlySubgraphIsExcludedByDefaultAndOptInsAreIndependent()
    {
        var system = N("package:system.runtime", NodeKind.Package) with { Label = "System.Runtime" };
        var thirdParty = N("package:swashbuckle", NodeKind.Package) with { Label = "Swashbuckle.AspNetCore" };
        var graph = new DependencyGraph { Root = "/repo", Nodes = [system, thirdParty], Edges = [E("external", system.Id, thirdParty.Id, EdgeKind.PackageDependency)] };

        var defaults = CommunityProjectionBuilder.Build(graph, new());
        Assert.Empty(defaults.Vertices);
        Assert.Equal(1, defaults.Metadata.ExcludedSystemPackageCount);
        Assert.Equal(1, defaults.Metadata.ExcludedThirdPartyPackageCount);

        var thirdPartyOnly = CommunityProjectionBuilder.Build(graph, new() { IncludeThirdPartyPackages = true });
        Assert.Single(thirdPartyOnly.Vertices);
        Assert.Equal(CommunityNodeOwnership.SystemPackage, thirdPartyOnly.Ownership[system.Id]);
        Assert.DoesNotContain(system.Id, thirdPartyOnly.OriginalToVertex.Keys);
        Assert.Contains(thirdParty.Id, thirdPartyOnly.OriginalToVertex.Keys);

        var systemOnly = CommunityProjectionBuilder.Build(graph, new() { IncludeSystemPackages = true });
        Assert.Single(systemOnly.Vertices);
        Assert.Contains(system.Id, systemOnly.OriginalToVertex.Keys);
        Assert.DoesNotContain(thirdParty.Id, systemOnly.OriginalToVertex.Keys);
    }

    [Fact]
    public void CompatibleExternalPathContractsButIncompatibleContextDoesNot()
    {
        var source = N("project:source", NodeKind.Project); var target = N("project:target", NodeKind.Project);
        var produced = N("package:target", NodeKind.Package); var external = N("package:external", NodeKind.Package);
        GraphEdge[] Edges(string secondTfm) =>
        [
            E("source-external", source.Id, external.Id, EdgeKind.PackageReference, "source", "net8.0"),
            E("external-target", external.Id, produced.Id, EdgeKind.PackageDependency, "source", secondTfm),
            E("producer", target.Id, produced.Id, EdgeKind.ProducesPackage)
        ];

        var compatible = CommunityProjectionBuilder.Build(new() { Root = "/repo", Nodes = [source, target, produced, external], Edges = Edges("net8.0") }, new());
        var contracted = Assert.Single(compatible.Edges);
        Assert.True(contracted.Contracted); Assert.Equal(.25, contracted.Weight); Assert.Equal(1, contracted.MinimumHiddenHops);

        var incompatible = CommunityProjectionBuilder.Build(new() { Root = "/repo", Nodes = [source, target, produced, external], Edges = Edges("net9.0") }, new());
        Assert.Empty(incompatible.Edges);
    }

    [Fact]
    public void SharedExternalHubDoesNotCreateSourceToSourceEdges()
    {
        var left = N("project:left", NodeKind.Project); var right = N("project:right", NodeKind.Project); var hub = N("package:hub", NodeKind.Package);
        var graph = new DependencyGraph { Root = "/repo", Nodes = [left, right, hub], Edges = [E("left-hub", left.Id, hub.Id, EdgeKind.PackageReference), E("right-hub", right.Id, hub.Id, EdgeKind.PackageReference)] };
        var projection = CommunityProjectionBuilder.Build(graph, new());
        Assert.Equal(2, projection.Vertices.Count); Assert.Empty(projection.Edges); Assert.Equal(0, projection.Metadata.ContractedEdgeCount);
    }

    [Fact]
    public void ConfiguredUnmappedInternalPackageRequiresExplicitOptIn()
    {
        var package = N("package:acme.shared", NodeKind.Package) with { Label = "Acme.Shared" };
        var excluded = CommunityProjectionBuilder.Build(new() { Root = "/repo", Nodes = [package], Edges = [] }, new() { InternalPackagePatterns = ["Acme.*"] });
        Assert.Equal(CommunityNodeOwnership.UnmappedInternalPackage, excluded.Ownership[package.Id]); Assert.Empty(excluded.Vertices);

        var included = CommunityProjectionBuilder.Build(new() { Root = "/repo", Nodes = [package], Edges = [] }, new() { InternalPackagePatterns = ["Acme.*"], IncludeUnmappedInternalPackages = true });
        Assert.Single(included.Vertices); Assert.Equal(1, included.Metadata.IncludedUnmappedInternalPackageCount);
    }

    [Fact]
    public void ProducerPairIsCollapsedExactlyOnce()
    {
        var project = N("project:source", NodeKind.Project); var package = N("package:source", NodeKind.Package);
        var graph = new DependencyGraph { Root = "/repo", Nodes = [project, package], Edges = [E("producer-a", project.Id, package.Id, EdgeKind.ProducesPackage), E("producer-b", project.Id, package.Id, EdgeKind.ProducesPackage)] };
        var projection = CommunityProjectionBuilder.Build(graph, new());
        Assert.Single(projection.Vertices); Assert.Equal(1, projection.Metadata.CollapsedProducerPairCount);
        Assert.Equal(projection.OriginalToVertex[project.Id], projection.OriginalToVertex[package.Id]);
    }

    private static GraphNode N(string id, NodeKind kind) => new() { Id = id, Label = id, Kind = kind, Path = kind == NodeKind.Project ? id.Replace("project:", "", StringComparison.Ordinal) + ".csproj" : null };
    private static GraphEdge E(string id, string source, string target, EdgeKind kind, string owner = "", string tfm = "") => new()
    {
        Id = id,
        Source = source,
        Target = target,
        Kind = kind,
        Contexts = owner.Length == 0 && tfm.Length == 0 ? [] : [new(owner, "assets.json", tfm, null, null, null, true)]
    };
}
