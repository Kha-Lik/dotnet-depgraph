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
