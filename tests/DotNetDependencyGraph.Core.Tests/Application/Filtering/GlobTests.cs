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

public sealed class GlobTests
{
    [Theory]
    [InlineData("Company.Core", "Company.*", true)]
    [InlineData("company.Core", "COMPANY.*", true)]
    [InlineData("Another.Internal.XCore2", "Another.Internal.?Core*", true)]
    [InlineData("Company/Core", "Company/*", true)]
    [InlineData("Company.Core", "Company.?ore", true)]
    [InlineData("External.Core", "Company.*", false)]
    public void MatchesExpected(string value, string pattern, bool expected) => Assert.Equal(expected, Glob.IsMatch(value, pattern));

    [Fact] public void NormalizesWindowsAndUnixSeparators() => Assert.Equal("src/App/App.csproj", ProjectDiscovery.Normalize(@"./src\App/App.csproj"));
}
