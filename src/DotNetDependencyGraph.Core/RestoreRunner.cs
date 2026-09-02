using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotNetDependencyGraph.Core;

public enum RestoreMode { Never, Missing, Always }
public sealed record RestoreResult(string ProjectPath, int ExitCode, string Output);

public static class RestoreRunner
{
    public static async Task<IReadOnlyList<RestoreResult>> RestoreAsync(IEnumerable<ProjectMetadata> projects, RestoreMode mode,
        int jobs, IReadOnlyDictionary<string, string> properties, CancellationToken cancellationToken)
    {
        if (mode == RestoreMode.Never) return [];
        var targets = projects.Where(x => mode == RestoreMode.Always || !File.Exists(x.AssetsFile)).ToArray();
        var results = new ConcurrentBag<RestoreResult>();
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = jobs, CancellationToken = cancellationToken }, async (project, ct) =>
        {
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add("restore"); info.ArgumentList.Add(project.FullPath);
            foreach (var property in properties.OrderBy(x => x.Key, StringComparer.Ordinal)) info.ArgumentList.Add($"-p:{property.Key}={property.Value}");
            using var process = new Process { StartInfo = info };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            results.Add(new(project.FullPath, process.ExitCode, Redact((await stdout) + (await stderr))));
        });
        return results.OrderBy(x => x.ProjectPath, ProjectDiscovery.PathComparer).ToArray();
    }

    private static string Redact(string value)
    {
        foreach (var marker in new[] { "password=", "token=", "apikey=", "api_key=" })
        {
            var start = 0;
            while ((start = value.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var secretStart = start + marker.Length; var end = value.IndexOfAny([' ', '\r', '\n', '&'], secretStart); if (end < 0) end = value.Length;
                value = value[..secretStart] + "***" + value[end..]; start = secretStart + 3;
            }
        }
        return value;
    }
}
