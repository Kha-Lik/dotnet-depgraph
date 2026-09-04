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
