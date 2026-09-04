using DotNetDependencyGraph.Core.Application.Filtering;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Infrastructure.MSBuild;

public sealed class ProjectDiscovery
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", ".hg", "bin", "obj", "node_modules" };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".csproj", ".fsproj", ".vbproj" };

    public (IReadOnlyList<string> Projects, IReadOnlyList<GraphDiagnostic> Diagnostics) Discover(
        string root, IReadOnlyList<string>? includes = null, IReadOnlyList<string>? excludes = null,
        string? output = null, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        includes ??= [];
        excludes ??= [];
        var projects = new List<string>();
        var diagnostics = new List<GraphDiagnostic>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current).Order(StringComparer.Ordinal))
                {
                    if (!Extensions.Contains(Path.GetExtension(file))) continue;
                    var rel = Normalize(Path.GetRelativePath(root, file));
                    if (includes.Count > 0 && !includes.Any(x => Glob.IsMatch(rel, x))) continue;
                    if (excludes.Any(x => Glob.IsMatch(rel, x))) continue;
                    projects.Add(Path.GetFullPath(file));
                }
                foreach (var directory in Directory.EnumerateDirectories(current).OrderDescending(StringComparer.Ordinal))
                {
                    var info = new DirectoryInfo(directory);
                    var rel = Normalize(Path.GetRelativePath(root, directory));
                    if (ExcludedNames.Contains(info.Name) || excludes.Any(x => Glob.IsMatch(rel, x) || Glob.IsMatch(rel + "/", x))) continue;
                    if (output is not null && Path.GetFullPath(directory).Equals(Path.GetFullPath(output), PathComparison)) continue;
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(directory);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                diagnostics.Add(new("discovery-inaccessible", DiagnosticSeverity.Warning, ex.Message, Path: current));
            }
        }
        projects.Sort(PathComparer);
        return (projects, diagnostics.OrderBy(x => x.Path, PathComparer).ToArray());
    }

    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
