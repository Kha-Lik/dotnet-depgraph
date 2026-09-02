using System.Diagnostics;
using System.Text.Json;

namespace DotNetDependencyGraph.Core;

/// <summary>Uses MSBuild's supported getProperty/getItem evaluation surface without building the project.</summary>
public sealed class ProjectEvaluator
{
    private readonly Dictionary<string, Evaluation> _cache = new(ProjectDiscovery.PathComparer);
    private const string Properties = "MSBuildProjectName,AssemblyName,PackageId,IsPackable,OutputType,TargetFramework,TargetFrameworks,MSBuildProjectExtensionsPath,ProjectAssetsFile,IsTestProject,UsingMicrosoftNETSdkWeb,AzureFunctionsVersion,ProjectSdk";

    public ProjectMetadata Evaluate(string projectPath, string root, IReadOnlyDictionary<string, string> globalProperties, CancellationToken cancellationToken = default)
    {
        var evaluation = Get(projectPath, globalProperties, cancellationToken); var p = evaluation.Properties;
        var name = Value(p, "MSBuildProjectName", Path.GetFileNameWithoutExtension(projectPath));
        var assembly = Value(p, "AssemblyName", name); var packageId = Value(p, "PackageId", assembly);
        var isPackable = bool.TryParse(Value(p, "IsPackable", "false"), out var packable) && packable;
        var outputType = Value(p, "OutputType", "Library");
        var tfms = Value(p, "TargetFrameworks", Value(p, "TargetFramework", ""))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = Value(p, "MSBuildProjectExtensionsPath", Path.Combine(Path.GetDirectoryName(projectPath)!, "obj"));
        if (!Path.IsPathRooted(extensions)) extensions = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, extensions));
        var assets = Value(p, "ProjectAssetsFile", Path.Combine(extensions, "project.assets.json"));
        if (!Path.IsPathRooted(assets)) assets = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, assets));
        var (classification, evidence) = Classify(evaluation, outputType, isPackable);
        var relative = ProjectDiscovery.Normalize(Path.GetRelativePath(root, projectPath));
        return new()
        {
            FullPath = Path.GetFullPath(projectPath),
            RelativePath = relative,
            Id = "project:" + relative,
            Name = name,
            AssemblyName = assembly,
            PackageId = packageId,
            IsPackable = isPackable,
            OutputType = outputType,
            Sdk = Value(p, "ProjectSdk", ""),
            TargetFrameworks = tfms,
            AssetsFile = Path.GetFullPath(assets),
            Classification = classification,
            ClassificationEvidence = evidence
        };
    }

    public IReadOnlyList<string> ProjectReferences(string projectPath, IReadOnlyDictionary<string, string> globalProperties, CancellationToken cancellationToken = default)
        => Get(projectPath, globalProperties, cancellationToken).ProjectReferences;

    private Evaluation Get(string projectPath, IReadOnlyDictionary<string, string> globalProperties, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(projectPath, out var cached)) return cached;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("msbuild"); info.ArgumentList.Add(projectPath); info.ArgumentList.Add("--nologo");
        info.ArgumentList.Add("-getProperty:" + Properties); info.ArgumentList.Add("-getItem:ProjectReference,PackageReference");
        foreach (var property in globalProperties.OrderBy(x => x.Key, StringComparer.Ordinal)) info.ArgumentList.Add($"-p:{property.Key}={property.Value}");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        using var registration = cancellationToken.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"MSBuild evaluation failed ({process.ExitCode}): {stderr.Trim()}");
        var jsonStart = stdout.IndexOf('{');
        if (jsonStart < 0) throw new InvalidOperationException("MSBuild evaluation returned no JSON object.");
        using var document = JsonDocument.Parse(stdout[jsonStart..]); var root = document.RootElement;
        var properties = root.GetProperty("Properties").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var references = new List<string>();
        if (root.TryGetProperty("Items", out var items) && items.TryGetProperty("ProjectReference", out var projectReferences))
            foreach (var item in projectReferences.EnumerateArray())
            {
                var identity = item.TryGetProperty("FullPath", out var full) ? full.GetString() : item.GetProperty("Identity").GetString();
                if (!string.IsNullOrWhiteSpace(identity)) references.Add(Path.GetFullPath(Path.IsPathRooted(identity) ? identity : Path.Combine(Path.GetDirectoryName(projectPath)!, identity)));
            }
        if (!globalProperties.ContainsKey("TargetFramework") && properties.TryGetValue("TargetFrameworks", out var targetFrameworks))
            foreach (var tfm in targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                references.AddRange(EvaluateProjectReferences(projectPath, globalProperties, tfm, cancellationToken));
        return _cache[projectPath] = new(properties, references.Distinct(ProjectDiscovery.PathComparer).Order(ProjectDiscovery.PathComparer).ToArray(), root.Clone());
    }

    private static IReadOnlyList<string> EvaluateProjectReferences(string projectPath, IReadOnlyDictionary<string, string> globalProperties, string tfm, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("msbuild"); info.ArgumentList.Add(projectPath); info.ArgumentList.Add("--nologo"); info.ArgumentList.Add("-getItem:ProjectReference"); info.ArgumentList.Add("-p:TargetFramework=" + tfm);
        foreach (var property in globalProperties.OrderBy(x => x.Key, StringComparer.Ordinal)) info.ArgumentList.Add($"-p:{property.Key}={property.Value}");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        using var registration = cancellationToken.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Framework-specific MSBuild evaluation failed ({process.ExitCode}): {stderr.Trim()}");
        var start = stdout.IndexOf('{'); if (start < 0) return []; using var document = JsonDocument.Parse(stdout[start..]);
        if (!document.RootElement.TryGetProperty("Items", out var items) || !items.TryGetProperty("ProjectReference", out var refs)) return [];
        return refs.EnumerateArray().Select(item => item.TryGetProperty("FullPath", out var full) ? full.GetString() : item.GetProperty("Identity").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.GetFullPath(Path.IsPathRooted(x!) ? x! : Path.Combine(Path.GetDirectoryName(projectPath)!, x!))).ToArray();
    }

    private static (string, string[]) Classify(Evaluation evaluation, string outputType, bool packable)
    {
        var p = evaluation.Properties; var evidence = new List<string>(); string classification;
        if (Value(p, "IsTestProject", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) { classification = "test-project"; evidence.Add("IsTestProject=true"); }
        else if (!string.IsNullOrWhiteSpace(Value(p, "AzureFunctionsVersion", "")) || HasPackage(evaluation, "Microsoft.NET.Sdk.Functions")) { classification = "azure-functions"; evidence.Add("Azure Functions MSBuild/package metadata"); }
        else if (Value(p, "UsingMicrosoftNETSdkWeb", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) { classification = "web-application"; evidence.Add("UsingMicrosoftNETSdkWeb=true"); }
        else if (!outputType.Equals("Library", StringComparison.OrdinalIgnoreCase)) { classification = "executable"; evidence.Add("OutputType=" + outputType); }
        else if (packable) { classification = "packable-library"; evidence.Add("IsPackable=true"); }
        else { classification = "class-library"; evidence.Add("OutputType=Library"); }
        return (classification, evidence.ToArray());
    }

    private static bool HasPackage(Evaluation evaluation, string name)
    {
        if (!evaluation.Raw.TryGetProperty("Items", out var items) || !items.TryGetProperty("PackageReference", out var packages)) return false;
        return packages.EnumerateArray().Any(x => x.GetProperty("Identity").GetString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
    }
    private static string Value(IReadOnlyDictionary<string, string> values, string name, string fallback)
        => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    private sealed record Evaluation(IReadOnlyDictionary<string, string> Properties, IReadOnlyList<string> ProjectReferences, JsonElement Raw);
}
