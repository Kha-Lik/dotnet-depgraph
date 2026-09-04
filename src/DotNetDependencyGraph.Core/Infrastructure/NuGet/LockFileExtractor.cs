using NuGet.Common;
using NuGet.ProjectModel;

using DotNetDependencyGraph.Core.Application.Scanning;
using DotNetDependencyGraph.Core.Domain.Graph;
using DotNetDependencyGraph.Core.Infrastructure.MSBuild;

namespace DotNetDependencyGraph.Core.Infrastructure.NuGet;

public sealed class LockFileExtractor
{
    public bool Extract(ProjectMetadata owner, IReadOnlyDictionary<string, ProjectMetadata> projectsByPath,
        GraphBuilder builder, IReadOnlyList<string> selectedTfms, IReadOnlyList<string> selectedRids)
    {
        LockFile lockFile;
        try { lockFile = LockFileUtilities.GetLockFile(owner.AssetsFile, NullLogger.Instance); }
        catch (Exception ex)
        {
            builder.Diagnostic(new("assets-malformed", DiagnosticSeverity.Error, ex.Message, owner.Id, owner.AssetsFile));
            return false;
        }
        if (lockFile is null || lockFile.PackageSpec is null || lockFile.Targets.Count == 0)
        {
            builder.Diagnostic(new("assets-malformed", DiagnosticSeverity.Error, "NuGet could not parse a package specification and at least one target from the assets file.", owner.Id, owner.AssetsFile));
            return false;
        }
        var recordedPath = lockFile.PackageSpec.RestoreMetadata?.ProjectPath;
        if (!string.IsNullOrWhiteSpace(recordedPath) && !Path.GetFullPath(recordedPath).Equals(owner.FullPath, ProjectDiscovery.PathComparison))
            builder.Diagnostic(new("assets-owner-mismatch", DiagnosticSeverity.Error, $"Assets owner is '{recordedPath}'.", owner.Id, owner.AssetsFile));

        var selectedTarget = false;
        foreach (var target in lockFile.Targets.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var tfm = target.TargetFramework.GetShortFolderName();
            var rid = target.RuntimeIdentifier;
            if (selectedTfms.Count > 0 && !selectedTfms.Contains("all", StringComparer.OrdinalIgnoreCase) && !selectedTfms.Contains(tfm, StringComparer.OrdinalIgnoreCase)) continue;
            if (selectedRids.Count > 0 && !selectedRids.Contains(rid ?? "", StringComparer.OrdinalIgnoreCase)) continue;
            selectedTarget = true;
            var libraries = target.Libraries.Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var directNames = DirectDependencies(lockFile, target, tfm);
            foreach (var library in target.Libraries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase)) continue;
                var libraryName = library.Name!;
                var packageId = PackageId(libraryName);
                builder.AddNode(new()
                {
                    Id = packageId,
                    Label = libraryName,
                    Kind = NodeKind.Package,
                    Versions = [library.Version!.ToNormalizedString()],
                    TargetFrameworks = [tfm],
                    RuntimeIdentifiers = rid is null ? [] : [rid]
                });
                if (directNames.TryGetValue(libraryName, out var requested))
                    builder.AddEdge(owner.Id, packageId, EdgeKind.PackageReference,
                        Context(owner, tfm, rid, requested, library.Version!.ToNormalizedString(), true));
                foreach (var dependency in library.Dependencies.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
                {
                    var requestedRange = dependency.VersionRange?.ToNormalizedString();
                    if (!libraries.TryGetValue(dependency.Id, out var resolved))
                    {
                        var unresolvedId = "unresolved-library:" + dependency.Id.ToLowerInvariant();
                        builder.AddNode(new() { Id = unresolvedId, Label = dependency.Id, Kind = NodeKind.UnresolvedLibrary, TargetFrameworks = [tfm] });
                        builder.AddEdge(packageId, unresolvedId, EdgeKind.PackageDependency, Context(owner, tfm, rid, requestedRange, null, false));
                        builder.Diagnostic(new("unresolved-target-library", DiagnosticSeverity.Warning,
                            $"'{dependency.Id}' is required by '{libraryName}' but absent from target '{target.Name}'.", owner.Id, owner.AssetsFile));
                    }
                    else if (string.Equals(resolved.Type, "package", StringComparison.OrdinalIgnoreCase))
                    {
                        var resolvedName = resolved.Name!;
                        var resolvedId = PackageId(resolvedName);
                        builder.AddNode(new()
                        {
                            Id = resolvedId,
                            Label = resolvedName,
                            Kind = NodeKind.Package,
                            Versions = [resolved.Version!.ToNormalizedString()],
                            TargetFrameworks = [tfm],
                            RuntimeIdentifiers = rid is null ? [] : [rid]
                        });
                        builder.AddEdge(packageId, resolvedId, EdgeKind.PackageDependency,
                            Context(owner, tfm, rid, requestedRange, resolved.Version!.ToNormalizedString(), false));
                    }
                    else
                    {
                        builder.Diagnostic(new("non-package-target-dependency", DiagnosticSeverity.Info,
                            $"Dependency '{dependency.Id}' resolves as NuGet library type '{resolved.Type}'.", owner.Id, owner.AssetsFile));
                    }
                }
            }
        }
        if (!selectedTarget) builder.Diagnostic(new("target-selection-empty", DiagnosticSeverity.Warning, "No restored target matched the requested TFM/RID selection.", owner.Id, owner.AssetsFile));
        return true;
    }

    private static Dictionary<string, string?> DirectDependencies(LockFile file, LockFileTarget target, string tfm)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var framework = file.PackageSpec?.TargetFrameworks.FirstOrDefault(x =>
            x.FrameworkName.Equals(target.TargetFramework) || x.FrameworkName.GetShortFolderName().Equals(tfm, StringComparison.OrdinalIgnoreCase));
        if (framework is null) return result;
        foreach (var dependency in framework.Dependencies)
            result[dependency.Name] = dependency.LibraryRange.VersionRange?.ToNormalizedString();
        return result;
    }

    private static EdgeContext Context(ProjectMetadata owner, string tfm, string? rid, string? requested, string? resolved, bool direct)
        => new(owner.Id, owner.AssetsFile, tfm, rid, requested, resolved, direct);
    public static string PackageId(string name) => "package:" + name.ToLowerInvariant();
}
