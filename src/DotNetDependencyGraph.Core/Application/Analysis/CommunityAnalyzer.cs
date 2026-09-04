using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using DotNetDependencyGraph.Core.Algorithms.Communities;
using DotNetDependencyGraph.Core.Domain.Communities;
using DotNetDependencyGraph.Core.Domain.Graph;

namespace DotNetDependencyGraph.Core.Application.Analysis;

public static partial class CommunityAnalyzer
{
    private static readonly HashSet<string> CommonTokens = new(StringComparer.OrdinalIgnoreCase)
    { "core", "common", "shared", "function", "functions", "service", "services", "project", "test", "tests", "src", "source", "library", "libraries" };

    public static DependencyGraph Analyze(DependencyGraph graph, CommunitySettings? settings = null, Action<string>? progress = null)
    {
        settings ??= new(); settings.Validate();
        progress?.Invoke($"Community analysis started: Leiden/CPM, seed {settings.Seed}, {settings.Trials} trial(s) × {settings.Levels} resolution level(s).");
        var projection = CommunityProjectionBuilder.Build(graph, settings);
        var vertexIds = projection.Vertices.Select(vertex => vertex.Id).ToArray();
        progress?.Invoke($"Community projection: {projection.Vertices.Count} vertex/vertices, {projection.Edges.Count} weighted edge(s), {projection.ExcludedTestNodeIds.Count} test node(s) deferred.");
        var gammas = ResolutionValues(settings).ToArray();
        var candidates = new List<Candidate>();
        foreach (var gamma in gammas)
        {
            progress?.Invoke($"Evaluating CPM resolution {gamma:0.########} across {settings.Trials} seeded trial(s).");
            var candidate = RunTrials(vertexIds, projection.Edges, gamma, settings); candidates.Add(candidate);
            progress?.Invoke($"CPM resolution {gamma:0.########}: {candidate.Membership.Values.Distinct().Count()} community/communities, quality {candidate.Result.Quality:0.######}, stability {candidate.Stability:0.######}, winning seed {candidate.Seed}.");
        }
        for (var i = 0; i < candidates.Count; i++)
        {
            var neighbor = i == 0 ? (candidates.Count > 1 ? candidates[1] : null) : candidates[i - 1];
            candidates[i] = candidates[i] with { NeighborSimilarity = neighbor is null ? null : AdjustedRand(candidates[i].Membership, neighbor.Membership) };
        }
        var standardIndex = SelectStandard(candidates, settings);

        var hierarchy = BuildHierarchy(graph, projection, settings, candidates, standardIndex);
        progress?.Invoke($"Strict hierarchy built: coarse/standard/fine = {hierarchy.Granularity["coarse"].Values.Distinct().Count()}/{hierarchy.Granularity["standard"].Values.Distinct().Count()}/{hierarchy.Granularity["fine"].Values.Distinct().Count()} groups; selected resolution {candidates[standardIndex].Gamma:0.########}.");
        var diagnostics = projection.Diagnostics.Concat(hierarchy.Diagnostics).ToList();
        var assignments = ExpandAssignments(graph, projection, hierarchy, settings, diagnostics);
        var standard = hierarchy.Granularity["standard"];
        var enrichedNodes = EnrichNodes(graph, assignments, standard);
        var enrichedGraph = graph with { Nodes = enrichedNodes };
        var communities = BuildCommunityRecords(enrichedGraph, hierarchy, assignments);
        var cross = CrossCommunity(enrichedGraph, standard);
        var cycles = QuotientCycles(cross);
        var paths = RunnablePaths(enrichedGraph);
        progress?.Invoke($"Architecture analysis complete: {cross.Length} directed cross-community relation(s), {cycles.Length} quotient cycle(s), {paths.Length} persisted runnable path(s).");
        var profile = candidates.Select(candidate => new CommunityResolutionCandidate
        {
            Resolution = candidate.Gamma,
            Quality = Round(candidate.Result.Quality),
            CommunityCount = candidate.Membership.Values.Distinct().Count(),
            Sizes = candidate.Membership.Values.GroupBy(value => value).Select(group => group.Count()).Order().ToArray(),
            SingletonCount = candidate.Membership.Values.GroupBy(value => value).Count(group => group.Count() == 1),
            Connected = candidate.Result.Connected,
            WinningSeed = candidate.Seed,
            Trials = settings.Trials,
            Stability = Round(candidate.Stability),
            NeighborSimilarity = candidate.NeighborSimilarity is null ? null : Round(candidate.NeighborSimilarity.Value)
            ,
            SelectedStandard = candidate == candidates[standardIndex]
        }).ToArray();
        foreach (var candidate in profile.Where(candidate => !candidate.Connected))
            diagnostics.Add(new("community-disconnected", DiagnosticSeverity.Error, $"Leiden result at resolution {candidate.Resolution} was disconnected."));
        foreach (var candidate in profile.Where(candidate => candidate.Stability < .6 && candidate.CommunityCount > 1))
            diagnostics.Add(new("community-partition-unstable", DiagnosticSeverity.Info, $"Partition at resolution {candidate.Resolution} has trial stability {candidate.Stability:0.###}."));
        foreach (var candidate in profile.Where(candidate => candidate.CommunityCount > 1 && candidate.SingletonCount > candidate.CommunityCount / 2))
            diagnostics.Add(new("community-excessive-singletons", DiagnosticSeverity.Info, $"Resolution {candidate.Resolution} produced {candidate.SingletonCount} singleton communities out of {candidate.CommunityCount}."));
        if (projection.Vertices.Count > 0 && projection.Edges.Count == 0)
            diagnostics.Add(new("community-no-meaningful-structure", DiagnosticSeverity.Info, "The detection projection has no eligible dependency edges; isolated vertices remain separate."));
        if (communities.Any(community => community.NameConfidence < .35))
            diagnostics.Add(new("community-name-low-confidence", DiagnosticSeverity.Info, "One or more inferred community names have low confidence; representative labels are used."));
        if (settings.TargetSize is int target && profile.All(item => item.Sizes.Count == 0 || Median(item.Sizes) > target * 2))
            diagnostics.Add(new("community-target-size-unsupported", DiagnosticSeverity.Info, $"The requested target size {target} is not supported by a stable topology split in the evaluated resolution profile."));

        var analysis = new CommunityAnalysis
        {
            GraphFingerprint = GraphFingerprint.Calculate(graph),
            Settings = settings,
            ProjectionRules = new() { ExcludeTests = !settings.IncludeTestsInDetection, ExcludeUnresolved = !settings.IncludeUnresolved },
            ResolutionProfile = profile,
            Communities = communities,
            NodeAssignments = assignments,
            GranularityAssignments = hierarchy.Granularity.ToDictionary(item => item.Key,
                item => (IReadOnlyDictionary<string, string>)item.Value, StringComparer.Ordinal),
            CrossCommunityDependencies = cross,
            CommunityCycles = cycles,
            RunnableImpactPaths = paths,
            Diagnostics = diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.ProjectId, StringComparer.Ordinal).ToArray()
        };
        progress?.Invoke($"Community analysis complete with {analysis.Diagnostics.Count} diagnostic(s).");
        return enrichedGraph with { SchemaVersion = "2.0", CommunityAnalysis = analysis };
    }

    private static IEnumerable<double> ResolutionValues(CommunitySettings settings)
    {
        if (settings.Levels == 1) return [settings.Resolution];
        var result = new SortedSet<double>();
        for (var i = 0; i < settings.Levels; i++)
        {
            var exponent = i - (settings.Levels - 1) / 2d;
            result.Add(Math.Round(settings.Resolution * Math.Pow(2, exponent), 8));
        }
        return result;
    }

    private static Candidate RunTrials(string[] vertices, IReadOnlyList<CommunityProjectionEdge> edges, double gamma, CommunitySettings settings)
    {
        var trials = new List<(LeidenCpm.Result Result, int Seed)>();
        for (var trial = 0; trial < settings.Trials; trial++)
        {
            var seed = unchecked(settings.Seed + trial * 104729);
            trials.Add((LeidenCpm.Detect(vertices, edges, gamma, seed), seed));
        }
        var winner = trials.OrderByDescending(trial => trial.Result.Quality)
            .ThenBy(trial => Signature(trial.Result.Membership), StringComparer.Ordinal).ThenBy(trial => trial.Seed).First();
        var stability = trials.Count == 1 ? 1 : trials.Average(trial => AdjustedRand(winner.Result.Membership, trial.Result.Membership));
        return new(gamma, winner.Result, winner.Result.Membership, winner.Seed, stability, null);
    }

    private static int SelectStandard(List<Candidate> candidates, CommunitySettings settings)
    {
        var target = settings.TargetSize;
        return candidates.Select((candidate, index) =>
        {
            var sizes = candidate.Membership.Values.GroupBy(value => value).Select(group => group.Count()).Order().ToArray();
            var singletonRatio = sizes.Length == 0 ? 0 : (double)sizes.Count(size => size == 1) / sizes.Length;
            var targetFit = target is null || sizes.Length == 0 ? 0 : -Math.Abs(Math.Log((Median(sizes) + 1d) / (target.Value + 1d)));
            var resolutionFit = -Math.Abs(Math.Log(candidate.Gamma / settings.Resolution));
            var score = candidate.Stability + (candidate.NeighborSimilarity ?? 1) * .25 + targetFit * .15 + resolutionFit * .2 - singletonRatio * .5;
            return (index, score);
        }).OrderByDescending(item => item.score).ThenBy(item => Math.Abs(candidates[item.index].Gamma - settings.Resolution)).ThenBy(item => item.index).First().index;
    }

    private static HierarchyResult BuildHierarchy(DependencyGraph graph, CommunityProjection projection, CommunitySettings settings, List<Candidate> candidates, int standardIndex)
    {
        var levels = new List<Dictionary<string, int>>(); var diagnostics = new List<GraphDiagnostic>();
        Dictionary<string, int>? parent = null;
        foreach (var candidate in candidates)
        {
            Dictionary<string, int> membership;
            if (parent is null) membership = new(candidate.Membership, StringComparer.Ordinal);
            else
            {
                membership = new(StringComparer.Ordinal); var next = 0;
                foreach (var parentGroup in parent.GroupBy(item => item.Value).OrderBy(group => group.Min(item => item.Key), StringComparer.Ordinal))
                {
                    var members = parentGroup.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
                    if (members.Length < Math.Max(4, (settings.MinSize ?? 2) * 2))
                    { foreach (var member in members) membership[member] = next; next++; continue; }
                    var memberSet = members.ToHashSet(StringComparer.Ordinal);
                    var edges = projection.Edges.Where(edge => memberSet.Contains(edge.Source) && memberSet.Contains(edge.Target)).ToArray();
                    var split = RunTrials(members, edges, candidate.Gamma, settings);
                    var one = members.ToDictionary(member => member, _ => 0, StringComparer.Ordinal);
                    var oneQuality = LeidenCpm.Quality(members, edges, one, candidate.Gamma);
                    var groups = split.Membership.GroupBy(item => item.Value).OrderBy(group => group.Min(item => item.Key), StringComparer.Ordinal).ToArray();
                    var supported = groups.Length > 1 && split.Result.Connected && split.Result.Quality > oneQuality + 1e-9 && split.Stability >= .5;
                    if (!supported)
                    {
                        diagnostics.Add(new("community-hierarchy-split-rejected", DiagnosticSeverity.Info,
                            $"A {members.Length}-vertex community remained a leaf at resolution {candidate.Gamma:0.########}: split lacked stable objective support."));
                        foreach (var member in members) membership[member] = next; next++; continue;
                    }
                    foreach (var group in groups) { foreach (var member in group.Select(item => item.Key)) membership[member] = next; next++; }
                }
            }
            levels.Add(membership); parent = membership;
        }

        var keysByLevel = new List<Dictionary<string, string>>();
        for (var depth = 0; depth < levels.Count; depth++)
        {
            var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in levels[depth].GroupBy(item => item.Value).OrderBy(group => group.Min(item => item.Key), StringComparer.Ordinal))
            {
                var anchorVertex = group.Select(item => item.Key).Min(StringComparer.Ordinal)!;
                var originalAnchor = projection.Vertices.First(vertex => vertex.Id == anchorVertex).OriginalNodeIds.Min(StringComparer.Ordinal)!;
                string? parentKey = null;
                if (depth > 0) parentKey = keysByLevel[depth - 1][anchorVertex];
                var unchangedLeaf = parentKey is not null && group.Count() == keysByLevel[depth - 1].Count(item => item.Value == parentKey);
                var key = unchangedLeaf ? parentKey! : CommunityKey(parentKey, originalAnchor);
                foreach (var item in group) keyMap[item.Key] = key;
            }
            keysByLevel.Add(keyMap);
        }
        var coarseVertices = keysByLevel[0];
        var standardVertices = keysByLevel[standardIndex];
        var fineVertices = keysByLevel[^1];
        Dictionary<string, string> Expand(Dictionary<string, string> vertexMap) => projection.OriginalToVertex.ToDictionary(item => item.Key, item => vertexMap[item.Value], StringComparer.Ordinal);
        var granularity = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        { ["coarse"] = Expand(coarseVertices), ["standard"] = Expand(standardVertices), ["fine"] = Expand(fineVertices) };
        return new(levels, keysByLevel, granularity, candidates, diagnostics, standardIndex);
    }

    private static Dictionary<string, NodeCommunityAssignment> ExpandAssignments(DependencyGraph graph, CommunityProjection projection,
        HierarchyResult hierarchy, CommunitySettings settings, List<GraphDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, NodeCommunityAssignment>(StringComparer.Ordinal);
        foreach (var item in projection.OriginalToVertex.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var path = hierarchy.Keys.Select(keys => keys[item.Value]).Distinct(StringComparer.Ordinal).ToArray();
            result[item.Key] = new() { NodeId = item.Key, DetectedCommunityPath = path };
        }
        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var outgoing = graph.Edges.Where(GraphAnalysis.IsDependency).GroupBy(edge => edge.Source).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var testId in projection.ExcludedTestNodeIds)
        {
            var scores = new Dictionary<string, double>(StringComparer.Ordinal); var evidence = new List<string>();
            var queue = new Queue<(string Id, int Depth)>(); var seen = new HashSet<string>(StringComparer.Ordinal) { testId }; queue.Enqueue((testId, 0));
            while (queue.Count > 0)
            {
                var (id, depth) = queue.Dequeue(); if (depth >= 4 || !outgoing.TryGetValue(id, out var edges)) continue;
                foreach (var edge in edges.OrderBy(edge => edge.Target, StringComparer.Ordinal))
                {
                    if (result.TryGetValue(edge.Target, out var assignment))
                    {
                        var community = assignment.DetectedCommunityPath.Last();
                        var weight = EdgeWeight(edge.Kind, settings.EdgeWeights) / (depth + 1);
                        scores[community] = scores.GetValueOrDefault(community) + weight;
                        evidence.Add($"{edge.Kind} to {edge.Target} at distance {depth + 1}: {weight:0.###}");
                    }
                    else if (seen.Add(edge.Target) && nodeById.ContainsKey(edge.Target)) queue.Enqueue((edge.Target, depth + 1));
                }
            }
            var ranked = scores.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
            if (ranked.Length > 0 && (ranked.Length == 1 || ranked[0].Value > ranked[1].Value + 1e-9))
            {
                var exemplar = result.Values.First(value => value.DetectedCommunityPath.Last() == ranked[0].Key);
                result[testId] = new()
                {
                    NodeId = testId,
                    DetectedCommunityPath = exemplar.DetectedCommunityPath,
                    AssignmentSource = "inherited-test",
                    AssignmentEvidence = evidence.Order(StringComparer.Ordinal).ToArray(),
                    AssignmentScore = Round(ranked[0].Value),
                    CandidateCommunities = ranked.Select(item => item.Key).ToArray()
                };
            }
            else
            {
                var key = CommunityKey(null, "unassigned-tests:" + graph.Nodes.First(node => node.Id == testId).Component);
                result[testId] = new()
                {
                    NodeId = testId,
                    DetectedCommunityPath = [key],
                    AssignmentSource = "inherited-test",
                    AssignmentEvidence = evidence.Order(StringComparer.Ordinal).ToArray(),
                    AssignmentScore = ranked.FirstOrDefault().Value,
                    CandidateCommunities = ranked.Select(item => item.Key).ToArray()
                };
                diagnostics.Add(new(ranked.Length == 0 ? "community-test-unassigned" : "community-test-assignment-tie", DiagnosticSeverity.Info,
                    ranked.Length == 0 ? "Test project has no production dependency evidence." : "Test project has an exact community-assignment tie.", testId));
            }
        }
        foreach (var node in graph.Nodes.Where(node => !result.ContainsKey(node.Id)))
        {
            var key = CommunityKey(null, "excluded:" + node.Id);
            result[node.Id] = new() { NodeId = node.Id, DetectedCommunityPath = [key], AssignmentSource = "automatic", AssignmentEvidence = ["excluded from detection projection"] };
        }
        foreach (var granularity in hierarchy.Granularity.Values)
            foreach (var assignment in result.Values) if (!granularity.ContainsKey(assignment.NodeId)) granularity[assignment.NodeId] = assignment.DetectedCommunityPath.Last();
        return result;
    }

    private static GraphNode[] EnrichNodes(DependencyGraph graph, IReadOnlyDictionary<string, NodeCommunityAssignment> assignments, IReadOnlyDictionary<string, string> standard)
    {
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var dependencies = graph.Edges.Where(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived && ids.Contains(edge.Source) && ids.Contains(edge.Target)).ToArray();
        var outgoing = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var incoming = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in dependencies) { outgoing[edge.Source].Add(edge.Target); incoming[edge.Target].Add(edge.Source); }
        var runnable = graph.Nodes.Where(IsRunnable).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        // Share the added blast-radius work by walking once per runnable root,
        // rather than repeating one reverse transitive walk for every node.
        // Per-root visited sets make SCCs finite and preserve no-self-counting.
        var affectedBy = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var app in runnable.Order(StringComparer.Ordinal))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var stack = new Stack<string>(); stack.Push(app);
            while (stack.Count > 0)
            {
                var current = stack.Pop(); if (!seen.Add(current)) continue;
                if (current != app) affectedBy[current].Add(app);
                foreach (var dependency in outgoing[current]) stack.Push(dependency);
            }
        }
        var articulation = ArticulationPoints(ids, outgoing);
        var between = Betweenness(ids, outgoing);
        return graph.Nodes.Select(node =>
        {
            var apps = affectedBy[node.Id].Order(StringComparer.Ordinal).ToArray();
            var incident = outgoing[node.Id].Concat(incoming[node.Id]).ToArray();
            var neighboring = incident.Where(standard.ContainsKey).Select(id => standard[id]).Where(key => key != standard[node.Id]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var cross = incident.Count(id => standard.TryGetValue(id, out var key) && key != standard[node.Id]);
            var degree = incident.Length;
            var evidence = new List<string>(); string role;
            if (CommunityProjectionBuilder.IsTest(node)) role = "test-only";
            else if (IsRunnable(node)) role = "runnable entry point";
            else if (degree == 0) role = "isolated";
            else if (outgoing[node.Id].Count == 0 && incoming[node.Id].Count <= 1) role = "leaf";
            else if (neighboring.Length >= 2 && (articulation.Contains(node.Id) || between.GetValueOrDefault(node.Id) > 0)) role = "cross-community bridge";
            else if (apps.Length >= 2 && incoming[node.Id].Count >= outgoing[node.Id].Count) role = "foundational dependency";
            else if (neighboring.Length >= 2) role = "shared infrastructure";
            else role = "feature-local";
            if (articulation.Contains(node.Id)) evidence.Add("articulation point in undirected dependency projection");
            if (neighboring.Length > 0) evidence.Add($"neighbors {neighboring.Length} communities across {cross} incident edges");
            if (apps.Length > 0) evidence.Add($"reachable from {apps.Length} runnable projects");
            return node with
            {
                RunnableDependentCount = apps.Length,
                RunnableDependentIds = apps,
                AffectedCommunityCount = apps.Select(id => standard[id]).Distinct(StringComparer.Ordinal).Count(),
                AffectedComponentCount = apps.Select(id => graph.Nodes.First(n => n.Id == id).Component).Distinct().Count(),
                BetweennessCentrality = Round(between.GetValueOrDefault(node.Id)),
                NeighboringCommunityCount = neighboring.Length,
                CrossCommunityEdgeCount = cross,
                CrossCommunityEdgeRatio = degree == 0 ? 0 : Round((double)cross / degree),
                ArticulationPoint = articulation.Contains(node.Id),
                ArchitecturalRole = role,
                RoleEvidence = evidence
            };
        }).OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
    }

    private static CommunityRecord[] BuildCommunityRecords(DependencyGraph graph, HierarchyResult hierarchy, IReadOnlyDictionary<string, NodeCommunityAssignment> assignments)
    {
        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var allKeys = assignments.Values.SelectMany(assignment => assignment.DetectedCommunityPath).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var records = new List<CommunityRecord>();
        foreach (var key in allKeys.Order(StringComparer.Ordinal))
        {
            var members = assignments.Values.Where(assignment => assignment.DetectedCommunityPath.Contains(key)).Select(assignment => assignment.NodeId).Order(StringComparer.Ordinal).ToArray();
            var depth = assignments.Values.Where(assignment => assignment.DetectedCommunityPath.Contains(key)).Select(assignment => assignment.DetectedCommunityPath.IndexOf(key)).DefaultIfEmpty(0).Min();
            var parent = assignments.Values.First(assignment => assignment.DetectedCommunityPath.Contains(key)).DetectedCommunityPath.Take(depth).LastOrDefault();
            var children = assignments.Values.Where(assignment => assignment.DetectedCommunityPath.Contains(key) && assignment.DetectedCommunityPath.Count > depth + 1)
                .Select(assignment => assignment.DetectedCommunityPath[depth + 1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var memberSet = members.ToHashSet(StringComparer.Ordinal);
            var internalEdges = graph.Edges.Where(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived && memberSet.Contains(edge.Source) && memberSet.Contains(edge.Target)).ToArray();
            var outgoing = graph.Edges.Count(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived && memberSet.Contains(edge.Source) && !memberSet.Contains(edge.Target));
            var incoming = graph.Edges.Count(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived && !memberSet.Contains(edge.Source) && memberSet.Contains(edge.Target));
            var naming = Name(graph, members);
            var gamma = hierarchy.Candidates[Math.Min(depth, hierarchy.Candidates.Count - 1)].Gamma;
            var possible = members.Length * (members.Length - 1) / 2d;
            var colors = Colors(key, parent);
            records.Add(new()
            {
                StableKey = key,
                ParentKey = parent,
                Depth = depth,
                MemberNodeIds = members,
                DirectChildKeys = children,
                Resolution = gamma,
                Quality = Round(hierarchy.Candidates[Math.Min(depth, hierarchy.Candidates.Count - 1)].Result.Quality),
                Stability = Round(hierarchy.Candidates[Math.Min(depth, hierarchy.Candidates.Count - 1)].Stability),
                Size = members.Length,
                ProjectCount = members.Count(id => nodeById[id].Kind == NodeKind.Project),
                PackageCount = members.Count(id => nodeById[id].Kind == NodeKind.Package),
                RunnableCount = members.Count(id => IsRunnable(nodeById[id])),
                Name = naming.Name,
                NameConfidence = naming.Confidence,
                NameEvidence = naming.Evidence,
                Color = colors.Fill,
                BorderColor = colors.Border,
                InternalEdgeCount = internalEdges.Length,
                WeightedDensity = possible <= 0 ? 0 : Round(internalEdges.Length / possible),
                OutgoingEdgeCount = outgoing,
                IncomingEdgeCount = incoming,
                CouplingRatio = internalEdges.Length + incoming + outgoing == 0 ? 0 : Round((double)(incoming + outgoing) / (internalEdges.Length + incoming + outgoing)),
                RepresentativeNodeIds = members.OrderByDescending(id => nodeById[id].Centrality).ThenBy(id => id, StringComparer.Ordinal).Take(5).ToArray(),
                BridgeNodeIds = members.Where(id => nodeById[id].ArchitecturalRole is "cross-community bridge" or "shared infrastructure").Order(StringComparer.Ordinal).ToArray(),
                InternalCycleCount = members.Count(id => nodeById[id].InCycle),
                VersionSkewedPackageCount = members.Count(id => nodeById[id].Kind == NodeKind.Package && nodeById[id].VersionSkew)
            });
        }
        return records.ToArray();
    }

    private static CrossCommunityDependency[] CrossCommunity(DependencyGraph graph, IReadOnlyDictionary<string, string> membership)
        => graph.Edges.Where(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived && membership.ContainsKey(edge.Source) && membership.ContainsKey(edge.Target) && membership[edge.Source] != membership[edge.Target])
            .GroupBy(edge => (Source: membership[edge.Source], Target: membership[edge.Target]))
            .OrderBy(group => group.Key.Source, StringComparer.Ordinal).ThenBy(group => group.Key.Target, StringComparer.Ordinal)
            .Select(group => new CrossCommunityDependency
            {
                SourceCommunity = group.Key.Source,
                TargetCommunity = group.Key.Target,
                EdgeCount = group.Count(),
                SourceNodeCount = group.Select(edge => edge.Source).Distinct(StringComparer.Ordinal).Count(),
                TargetNodeCount = group.Select(edge => edge.Target).Distinct(StringComparer.Ordinal).Count(),
                EdgeKinds = group.GroupBy(edge => edge.Kind.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                RepresentativeEdgeIds = group.Select(edge => edge.Id).Order(StringComparer.Ordinal).Take(5).ToArray(),
                UniqueContextCount = group.SelectMany(edge => edge.Contexts).Select(context => (context.OwnerProjectId, context.TargetFramework, context.RuntimeIdentifier)).Distinct().Count()
            }).ToArray();

    private static CommunityCycle[] QuotientCycles(IReadOnlyList<CrossCommunityDependency> cross)
    {
        var ids = cross.SelectMany(edge => new[] { edge.SourceCommunity, edge.TargetCommunity }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var outgoing = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in cross) outgoing[edge.SourceCommunity].Add(edge.TargetCommunity);
        var components = StrongComponents(ids, outgoing);
        return components.Where(component => component.Count > 1 || outgoing[component[0]].Contains(component[0]))
            .Select(component => new CommunityCycle { CommunityKeys = component, SupportingEdgeIds = cross.Where(edge => component.Contains(edge.SourceCommunity) && component.Contains(edge.TargetCommunity)).SelectMany(edge => edge.RepresentativeEdgeIds.Take(1)).Order(StringComparer.Ordinal).ToArray() }).ToArray();
    }

    private static DependencyPathRecord[] RunnablePaths(DependencyGraph graph)
    {
        var outgoing = graph.Edges.Where(edge => GraphAnalysis.IsDependency(edge) && !edge.Derived).GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.Kind).ToArray(), StringComparer.Ordinal);
        var paths = new List<DependencyPathRecord>();
        foreach (var runnable in graph.Nodes.Where(IsRunnable).OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var queue = new Queue<string>(); var distance = new Dictionary<string, int>(StringComparer.Ordinal) { [runnable.Id] = 0 };
            var previous = new Dictionary<string, List<(string Node, string Edge)>>(StringComparer.Ordinal); queue.Enqueue(runnable.Id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (!outgoing.TryGetValue(current, out var edges)) continue;
                foreach (var edge in edges)
                {
                    var nextDistance = distance[current] + 1;
                    if (!distance.TryGetValue(edge.Target, out var known)) { distance[edge.Target] = nextDistance; previous[edge.Target] = []; queue.Enqueue(edge.Target); known = nextDistance; }
                    if (known == nextDistance) previous[edge.Target].Add((current, edge.Id));
                }
            }
            foreach (var target in distance.Keys.Where(id => id != runnable.Id).Order(StringComparer.Ordinal))
            {
                var alternatives = new List<(string[] Nodes, string[] Edges)>();
                void Collect(string cursor, List<string> nodes, List<string> edgeIds)
                {
                    if (alternatives.Count >= 4) return;
                    if (cursor == runnable.Id) { var n = nodes.Append(cursor).Reverse().ToArray(); var e = edgeIds.AsEnumerable().Reverse().ToArray(); alternatives.Add((n, e)); return; }
                    foreach (var step in previous[cursor].OrderBy(item => item.Node, StringComparer.Ordinal).ThenBy(item => item.Edge, StringComparer.Ordinal))
                    { nodes.Add(cursor); edgeIds.Add(step.Edge); Collect(step.Node, nodes, edgeIds); nodes.RemoveAt(nodes.Count - 1); edgeIds.RemoveAt(edgeIds.Count - 1); }
                }
                Collect(target, [], []); var truncated = alternatives.Count > 3;
                foreach (var alternative in alternatives.Take(3)) paths.Add(new() { Source = runnable.Id, Target = target, NodeIds = alternative.Nodes, EdgeIds = alternative.Edges, Truncated = truncated });
            }
        }
        return paths.ToArray();
    }

    private static (string Name, double Confidence, CommunityNameEvidence[] Evidence) Name(DependencyGraph graph, string[] members)
    {
        var nodes = graph.Nodes.Where(node => members.Contains(node.Id, StringComparer.Ordinal)).ToArray();
        var globalDocumentFrequency = graph.Nodes.SelectMany(node => Tokens(node).Distinct(StringComparer.OrdinalIgnoreCase)).GroupBy(token => token, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var scores = nodes.SelectMany(node => Tokens(node).Distinct(StringComparer.OrdinalIgnoreCase).Select(token => (token, node.Id)))
            .Where(item => !CommonTokens.Contains(item.token) && item.token.Length > 2)
            .GroupBy(item => item.token, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Token = Capitalize(group.Key), Score = group.Count() * Math.Log((graph.Nodes.Count + 1d) / (globalDocumentFrequency[group.Key] + 1d)), Members = group.Select(item => item.Id).Order(StringComparer.Ordinal).Take(5).ToArray() })
            .OrderByDescending(item => item.Score).ThenBy(item => item.Token, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (scores.Length == 0)
        {
            var representative = nodes.OrderByDescending(node => node.Centrality).ThenBy(node => node.Label, StringComparer.Ordinal).FirstOrDefault()?.Label ?? "Empty community";
            return ($"Community anchored by {representative}", 0, []);
        }
        var confidence = scores[0].Score <= 0 ? .2 : Math.Min(1, scores[0].Score / (scores.Sum(score => Math.Max(0, score.Score)) + .0001) + .25);
        var name = confidence >= .65 ? scores[0].Token : string.Join(" · ", scores.Select(score => score.Token));
        return (name, Round(confidence), scores.Select(score => new CommunityNameEvidence(score.Token, Round(score.Score), score.Members)).ToArray());
    }

    private static IEnumerable<string> Tokens(GraphNode node)
    {
        var text = (node.Label + " " + (node.Path ?? "")).Replace(".csproj", "", StringComparison.OrdinalIgnoreCase);
        text = CamelBoundary().Replace(text, "$1 $2");
        return TokenSplit().Split(text).Where(token => token.Length > 0).Select(token => token.ToLowerInvariant());
    }

    private static (string Fill, string Border) Colors(string key, string? parent)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes((parent ?? "") + "|" + key));
        var fillHue = (hash[0] * 256 + hash[1]) % 360;
        var borderHue = (fillHue + 75 + hash[4] % 211) % 360;
        return (Hsl(fillHue, 72 + hash[2] % 20, 48 + hash[3] % 15), Hsl(borderHue, 90, hash[5] % 2 == 0 ? 76 : 36));
    }

    private static string Hsl(double hue, double saturation, double lightness)
    {
        saturation /= 100; lightness /= 100;
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }
    private static string CommunityKey(string? parent, string anchor)
        => "community:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((parent ?? "root") + "\n" + anchor)))[..16].ToLowerInvariant();
    private static double EdgeWeight(EdgeKind kind, CommunityEdgeWeights weights) => kind switch { EdgeKind.ProjectReference => weights.ProjectReference, EdgeKind.PackageReference => weights.PackageReference, EdgeKind.PackageDependency => weights.PackageDependency, _ => 0 };
    private static bool IsRunnable(GraphNode node) => node.Kind == NodeKind.Project && node.Classification is "executable" or "web-application" or "azure-functions";
    private static HashSet<string> ArticulationPoints(HashSet<string> ids, Dictionary<string, List<string>> directed)
    {
        var adjacency = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var item in directed) foreach (var target in item.Value) { adjacency[item.Key].Add(target); adjacency[target].Add(item.Key); }
        var time = 0; var discovered = new Dictionary<string, int>(); var low = new Dictionary<string, int>(); var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string vertex, string? parent)
        {
            discovered[vertex] = low[vertex] = ++time; var children = 0;
            foreach (var next in adjacency[vertex].Order(StringComparer.Ordinal))
            {
                if (!discovered.ContainsKey(next)) { children++; Visit(next, vertex); low[vertex] = Math.Min(low[vertex], low[next]); if (parent is not null && low[next] >= discovered[vertex]) result.Add(vertex); }
                else if (next != parent) low[vertex] = Math.Min(low[vertex], discovered[next]);
            }
            if (parent is null && children > 1) result.Add(vertex);
        }
        foreach (var id in ids.Order(StringComparer.Ordinal)) if (!discovered.ContainsKey(id)) Visit(id, null); return result;
    }

    private static Dictionary<string, double> Betweenness(HashSet<string> ids, Dictionary<string, List<string>> directed)
    {
        var adjacency = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var item in directed) foreach (var target in item.Value) { adjacency[item.Key].Add(target); adjacency[target].Add(item.Key); }
        var centrality = ids.ToDictionary(id => id, _ => 0d, StringComparer.Ordinal);
        foreach (var source in ids.Order(StringComparer.Ordinal))
        {
            var stack = new Stack<string>(); var predecessors = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
            var paths = ids.ToDictionary(id => id, _ => 0d, StringComparer.Ordinal); paths[source] = 1;
            var distance = ids.ToDictionary(id => id, _ => -1, StringComparer.Ordinal); distance[source] = 0;
            var queue = new Queue<string>(); queue.Enqueue(source);
            while (queue.Count > 0) { var v = queue.Dequeue(); stack.Push(v); foreach (var w in adjacency[v].Order(StringComparer.Ordinal)) { if (distance[w] < 0) { distance[w] = distance[v] + 1; queue.Enqueue(w); } if (distance[w] == distance[v] + 1) { paths[w] += paths[v]; predecessors[w].Add(v); } } }
            var dependency = ids.ToDictionary(id => id, _ => 0d, StringComparer.Ordinal);
            while (stack.Count > 0) { var w = stack.Pop(); foreach (var v in predecessors[w]) if (paths[w] > 0) dependency[v] += paths[v] / paths[w] * (1 + dependency[w]); if (w != source) centrality[w] += dependency[w]; }
        }
        foreach (var id in ids) centrality[id] /= 2; return centrality;
    }

    private static List<List<string>> StrongComponents(string[] ids, Dictionary<string, List<string>> outgoing)
    {
        var index = 0; var indices = new Dictionary<string, int>(); var low = new Dictionary<string, int>(); var stack = new Stack<string>(); var onStack = new HashSet<string>(); var result = new List<List<string>>();
        void Visit(string v) { indices[v] = low[v] = index++; stack.Push(v); onStack.Add(v); foreach (var w in outgoing[v].Distinct().Order(StringComparer.Ordinal)) { if (!indices.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); } else if (onStack.Contains(w)) low[v] = Math.Min(low[v], indices[w]); } if (low[v] != indices[v]) return; var component = new List<string>(); string w2; do { w2 = stack.Pop(); onStack.Remove(w2); component.Add(w2); } while (w2 != v); component.Sort(StringComparer.Ordinal); result.Add(component); }
        foreach (var id in ids) if (!indices.ContainsKey(id)) Visit(id); return result;
    }

    private static double AdjustedRand(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        var ids = left.Keys.Intersect(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(); if (ids.Length < 2) return 1;
        static double Pairs(int n) => n * (n - 1) / 2d;
        var cells = ids.GroupBy(id => (left[id], right[id])).Sum(group => Pairs(group.Count()));
        var a = ids.GroupBy(id => left[id]).Sum(group => Pairs(group.Count())); var b = ids.GroupBy(id => right[id]).Sum(group => Pairs(group.Count())); var total = Pairs(ids.Length);
        var expected = a * b / total; var maximum = (a + b) / 2; return Math.Abs(maximum - expected) < 1e-12 ? 1 : (cells - expected) / (maximum - expected);
    }
    private static string Signature(IReadOnlyDictionary<string, int> membership) => string.Join(";", membership.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "=" + item.Value.ToString(CultureInfo.InvariantCulture)));
    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    private static double Round(double value) => Math.Round(value, 6);
    private static double Median(IReadOnlyList<int> values) => values.Count == 0 ? 0 : values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2d;
    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)] private static partial Regex CamelBoundary();
    [GeneratedRegex("[^A-Za-z0-9]+", RegexOptions.CultureInvariant)] private static partial Regex TokenSplit();

    private sealed record Candidate(double Gamma, LeidenCpm.Result Result, IReadOnlyDictionary<string, int> Membership, int Seed, double Stability, double? NeighborSimilarity);
    private sealed record HierarchyResult(List<Dictionary<string, int>> Levels, List<Dictionary<string, string>> Keys,
        Dictionary<string, Dictionary<string, string>> Granularity, List<Candidate> Candidates, List<GraphDiagnostic> Diagnostics, int StandardIndex);
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        var comparer = EqualityComparer<T>.Default; for (var i = 0; i < values.Count; i++) if (comparer.Equals(values[i], value)) return i; return -1;
    }
}
