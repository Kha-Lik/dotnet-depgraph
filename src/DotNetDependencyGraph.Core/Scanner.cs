namespace DotNetDependencyGraph.Core;

public sealed record ScanResult(DependencyGraph Graph, IReadOnlyList<ProjectMetadata> Projects);

public sealed class Scanner
{
    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(options.Root);
        var discovery = new ProjectDiscovery().Discover(root, options.IncludePaths, options.ExcludePaths, options.OutputDirectory, cancellationToken);
        var builder = new GraphBuilder(root);
        foreach (var diagnostic in discovery.Diagnostics) builder.Diagnostic(diagnostic);
        var evaluator = new ProjectEvaluator(); var projects = new List<ProjectMetadata>(); var evaluated = 0;
        foreach (var path in discovery.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var metadata = evaluator.Evaluate(path, root, options.GlobalProperties, cancellationToken); projects.Add(metadata); evaluated++;
                builder.AddNode(new()
                {
                    Id = metadata.Id,
                    Label = metadata.Name,
                    Kind = NodeKind.Project,
                    Path = metadata.RelativePath,
                    Classification = metadata.Classification,
                    ClassificationEvidence = metadata.ClassificationEvidence,
                    TargetFrameworks = metadata.TargetFrameworks
                });
            }
            catch (Exception ex) { builder.Diagnostic(new("project-evaluation-failed", DiagnosticSeverity.Error, ex.Message, Path: path)); }
        }
        var byPath = projects.ToDictionary(x => x.FullPath, ProjectDiscovery.PathComparer);
        foreach (var project in projects)
        {
            try
            {
                foreach (var reference in evaluator.ProjectReferences(project.FullPath, options.GlobalProperties, cancellationToken))
                {
                    if (byPath.TryGetValue(reference, out var target))
                        builder.AddEdge(project.Id, target.Id, EdgeKind.ProjectReference,
                            new(project.Id, project.AssetsFile, "", null, null, null, true));
                    else
                    {
                        var withinRoot = IsWithin(reference, root);
                        var id = "unresolved-project:" + ProjectDiscovery.Normalize(reference).ToLowerInvariant();
                        builder.AddNode(new() { Id = id, Label = Path.GetFileNameWithoutExtension(reference), Kind = NodeKind.UnresolvedProject, Path = reference });
                        builder.AddEdge(project.Id, id, EdgeKind.ProjectReference, new(project.Id, project.AssetsFile, "", null, null, null, true));
                        builder.Diagnostic(new(withinRoot ? "unresolved-project-reference" : "outside-root-project-reference", DiagnosticSeverity.Warning,
                            $"Project reference '{reference}' was not among discovered projects.", project.Id, reference));
                    }
                }
            }
            catch (Exception ex) { builder.Diagnostic(new("project-reference-evaluation-failed", DiagnosticSeverity.Error, ex.Message, project.Id, project.FullPath)); }
        }

        AddProducerMappings(projects, builder);
        var extractor = new LockFileExtractor(); var validAssets = 0; var missing = 0; var malformed = 0;
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(project.AssetsFile))
            { missing++; builder.Diagnostic(new("assets-missing", DiagnosticSeverity.Error, "Assets file is missing; run restore or select a restore mode.", project.Id, project.AssetsFile)); continue; }
            if (extractor.Extract(project, byPath, builder, options.TargetFrameworks, options.RuntimeIdentifiers)) validAssets++; else malformed++;
        }
        var mismatched = builder.DiagnosticCount("assets-owner-mismatch");
        var complete = evaluated == discovery.Projects.Count && validAssets == projects.Count && !builder.HasErrors;
        var graph = builder.Build(new()
        {
            DiscoveredProjects = discovery.Projects.Count,
            EvaluatedProjects = evaluated,
            ProjectsWithValidAssets = validAssets,
            MissingAssets = missing,
            MalformedAssets = malformed,
            MismatchedAssets = mismatched,
            Complete = complete
        });
        var diagnostics = graph.Diagnostics.ToList();
        foreach (var node in graph.Nodes.Where(x => graph.Edges.All(e => e.Source != x.Id && e.Target != x.Id)))
        {
            var missingAssets = diagnostics.Any(x => x.ProjectId == node.Id && x.Code == "assets-missing");
            var targetExcluded = diagnostics.Any(x => x.ProjectId == node.Id && x.Code == "target-selection-empty");
            var ambiguous = diagnostics.Any(x => x.ProjectId == node.Id && x.Code == "ambiguous-package-producer");
            diagnostics.Add(new("isolated-node", DiagnosticSeverity.Info,
                missingAssets ? "Node is isolated because authoritative restore data is missing." : targetExcluded ? "Node is isolated because no restored target matched the TFM/RID selection." : ambiguous ? "Node is isolated and an ambiguous local package identity prevented producer mapping." : "Node has no resolved dependencies or dependents.", node.Id, node.Path));
        }
        return new(graph with { Diagnostics = diagnostics.OrderBy(x => x.Code).ThenBy(x => x.ProjectId).ToArray() }, projects);
    }

    private static void AddProducerMappings(List<ProjectMetadata> projects, GraphBuilder builder)
    {
        foreach (var group in projects.Where(x => x.IsPackable).GroupBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var producers = group.ToArray();
            if (producers.Length > 1)
            {
                foreach (var producer in producers) builder.Diagnostic(new("ambiguous-package-producer", DiagnosticSeverity.Warning,
                    $"Multiple local projects produce package ID '{group.Key}'; no mapping was inferred.", producer.Id, producer.FullPath));
                continue;
            }
            var package = LockFileExtractor.PackageId(group.Key); var p = producers[0];
            builder.AddNode(new() { Id = package, Label = group.Key, Kind = NodeKind.Package });
            builder.AddEdge(p.Id, package, EdgeKind.ProducesPackage, new(p.Id, p.AssetsFile, "", null, null, null, false));
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, ProjectDiscovery.PathComparison) && !Path.IsPathRooted(relative);
    }
}
