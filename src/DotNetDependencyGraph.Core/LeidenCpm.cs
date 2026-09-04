namespace DotNetDependencyGraph.Core;

/// <summary>
/// Deterministic managed Leiden implementation for the Constant Potts Model.
/// It implements local moving, subset-constrained refinement, aggregation and
/// repeated optimization. See Traag, Waltman &amp; van Eck (2019),
/// https://doi.org/10.1038/s41598-019-41695-z, and the CPM objective in
/// Traag, Van Dooren &amp; Nesterov (2011), https://doi.org/10.1103/PhysRevE.84.016114.
/// This is original code under the repository license; no third-party code was copied.
/// </summary>
public static class LeidenCpm
{
    public sealed record Result(IReadOnlyDictionary<string, int> Membership, double Quality, bool Connected);

    public static Result Detect(IReadOnlyList<string> nodeIds, IReadOnlyList<CommunityProjectionEdge> edges, double resolution, int seed)
    {
        if (!double.IsFinite(resolution) || resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        var ordered = nodeIds.Order(StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) return new(new Dictionary<string, int>(), 0, true);
        var index = ordered.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.Ordinal);
        var baseNetwork = Network.Create(ordered, edges, index);
        var final = Enumerable.Range(0, ordered.Length).ToArray();

        foreach (var component in Components(baseNetwork))
        {
            var network = baseNetwork.Induced(component);
            var initial = Enumerable.Range(0, network.Count).ToArray();
            var rng = new DeterministicRandom(unchecked((ulong)(uint)seed ^ StableHash(network.Ids[0])));
            int[] partition = initial;
            for (var level = 0; level < 100; level++)
            {
                partition = LocalMove(network, initial, resolution, rng, null);
                var refined = Refine(network, partition, resolution, rng);
                if (refined.Distinct().Count() == network.Count) break;
                var aggregateInitial = refined.Select((community, vertex) => (community, vertex)).GroupBy(item => item.community).OrderBy(group => group.Key)
                    .Select(group => partition[group.First().vertex]).ToArray();
                var aggregated = network.Aggregate(refined);
                initial = aggregateInitial;
                network = aggregated;
                if (network.Count == 1) { partition = [0]; break; }
            }

            for (var vertex = 0; vertex < network.Count; vertex++)
                foreach (var globalOriginal in network.Members[vertex])
                    final[globalOriginal] = partition[vertex];
        }

        final = SplitDisconnected(baseNetwork, final);
        final = Canonicalize(ordered, final);
        var membership = ordered.Select((id, i) => (id, community: final[i])).ToDictionary(item => item.id, item => item.community, StringComparer.Ordinal);
        return new(membership, Quality(baseNetwork, final, resolution), VerifyConnected(baseNetwork, final));
    }

    public static double Quality(IReadOnlyList<string> nodeIds, IReadOnlyList<CommunityProjectionEdge> edges, IReadOnlyDictionary<string, int> membership, double resolution)
    {
        var ordered = nodeIds.Order(StringComparer.Ordinal).ToArray();
        var index = ordered.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.Ordinal);
        var network = Network.Create(ordered, edges, index);
        return Quality(network, ordered.Select(id => membership[id]).ToArray(), resolution);
    }

    public static double MoveDelta(IReadOnlyList<string> nodeIds, IReadOnlyList<CommunityProjectionEdge> edges,
        IReadOnlyDictionary<string, int> membership, string nodeId, int targetCommunity, double resolution)
    {
        var ordered = nodeIds.Order(StringComparer.Ordinal).ToArray();
        var index = ordered.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.Ordinal);
        var network = Network.Create(ordered, edges, index); var p = ordered.Select(id => membership[id]).ToArray();
        var before = Quality(network, p, resolution); p[index[nodeId]] = targetCommunity;
        return Quality(network, p, resolution) - before;
    }

    private static int[] LocalMove(Network network, int[] initial, double gamma, DeterministicRandom rng, int[]? allowedParent)
    {
        var partition = Normalize(initial); var nextLabel = partition.Max() + 1;
        for (var pass = 0; pass < 100; pass++)
        {
            var changed = false;
            var sizes = partition.Distinct().ToDictionary(c => c, c => Enumerable.Range(0, network.Count).Where(i => partition[i] == c).Sum(i => network.Sizes[i]));
            foreach (var vertex in rng.Shuffle(Enumerable.Range(0, network.Count).ToArray()))
            {
                var current = partition[vertex]; var vertexSize = network.Sizes[vertex];
                var weights = new Dictionary<int, double>();
                foreach (var (neighbor, weight) in network.Adjacency[vertex])
                {
                    if (allowedParent is not null && allowedParent[neighbor] != allowedParent[vertex]) continue;
                    weights[partition[neighbor]] = weights.GetValueOrDefault(partition[neighbor]) + weight;
                }
                var currentWeight = weights.GetValueOrDefault(current);
                var candidates = weights.Keys.Where(candidate => candidate != current).Order().ToList();
                if (sizes[current] > vertexSize) candidates.Add(nextLabel);
                var best = current; var bestGain = 1e-12;
                foreach (var candidate in candidates)
                {
                    var targetSize = sizes.GetValueOrDefault(candidate);
                    var gain = weights.GetValueOrDefault(candidate) - currentWeight
                        - gamma * vertexSize * (targetSize - (sizes[current] - vertexSize));
                    if (gain > bestGain + 1e-12 || (Math.Abs(gain - bestGain) <= 1e-12 && candidate < best))
                    { bestGain = gain; best = candidate; }
                }
                if (best == current) continue;
                sizes[current] -= vertexSize; sizes[best] = sizes.GetValueOrDefault(best) + vertexSize;
                partition[vertex] = best; if (best == nextLabel) nextLabel++; changed = true;
            }
            if (!changed) break;
        }
        return Normalize(partition);
    }

    private static int[] Refine(Network network, int[] parent, double gamma, DeterministicRandom rng)
    {
        var singleton = Enumerable.Range(0, network.Count).ToArray();
        return LocalMove(network, singleton, gamma, rng, parent);
    }

    private static double Quality(Network network, int[] partition, double gamma)
    {
        var internalWeight = network.SelfLoops.Sum();
        foreach (var (left, right, weight) in network.Edges) if (partition[left] == partition[right]) internalWeight += weight;
        var penalty = partition.Distinct().Sum(community =>
        {
            var size = Enumerable.Range(0, network.Count).Where(i => partition[i] == community).Sum(i => network.Sizes[i]);
            return gamma * size * (size - 1) / 2;
        });
        return internalWeight - penalty;
    }

    private static int[] SplitDisconnected(Network network, int[] partition)
    {
        var result = new int[network.Count]; var label = 0;
        foreach (var community in partition.Distinct().Order())
        {
            var remaining = Enumerable.Range(0, network.Count).Where(i => partition[i] == community).ToHashSet();
            while (remaining.Count > 0)
            {
                var start = remaining.Min(); var stack = new Stack<int>(); stack.Push(start); remaining.Remove(start);
                while (stack.Count > 0)
                {
                    var vertex = stack.Pop(); result[vertex] = label;
                    foreach (var (neighbor, _) in network.Adjacency[vertex])
                        if (partition[neighbor] == community && remaining.Remove(neighbor)) stack.Push(neighbor);
                }
                label++;
            }
        }
        return result;
    }

    private static bool VerifyConnected(Network network, int[] partition)
    {
        foreach (var community in partition.Distinct())
        {
            var members = Enumerable.Range(0, network.Count).Where(i => partition[i] == community).ToHashSet();
            if (members.Count <= 1) continue;
            var seen = new HashSet<int>(); var stack = new Stack<int>(); stack.Push(members.Min());
            while (stack.Count > 0) { var v = stack.Pop(); if (!seen.Add(v)) continue; foreach (var (n, _) in network.Adjacency[v]) if (members.Contains(n)) stack.Push(n); }
            if (seen.Count != members.Count) return false;
        }
        return true;
    }

    private static IReadOnlyList<int[]> Components(Network network)
    {
        var remaining = Enumerable.Range(0, network.Count).ToHashSet(); var result = new List<int[]>();
        while (remaining.Count > 0)
        {
            var members = new List<int>(); var stack = new Stack<int>(); stack.Push(remaining.Min());
            while (stack.Count > 0) { var v = stack.Pop(); if (!remaining.Remove(v)) continue; members.Add(v); foreach (var (n, _) in network.Adjacency[v]) stack.Push(n); }
            result.Add(members.Order().ToArray());
        }
        return result;
    }

    private static int[] Canonicalize(string[] ids, int[] partition)
    {
        var ordered = partition.Select((community, i) => (community, id: ids[i])).GroupBy(item => item.community)
            .OrderBy(group => group.Min(item => item.id), StringComparer.Ordinal).Select((group, i) => (group.Key, i)).ToDictionary(item => item.Key, item => item.i);
        return partition.Select(community => ordered[community]).ToArray();
    }

    private static int[] Normalize(int[] partition)
    {
        var map = partition.Distinct().Order().Select((value, i) => (value, i)).ToDictionary(item => item.value, item => item.i);
        return partition.Select(value => map[value]).ToArray();
    }

    private static ulong StableHash(string value)
    {
        unchecked { ulong hash = 14695981039346656037UL; foreach (var c in value) hash = (hash ^ c) * 1099511628211UL; return hash; }
    }

    private sealed class DeterministicRandom(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;
        private ulong Next() { _state ^= _state << 13; _state ^= _state >> 7; _state ^= _state << 17; return _state; }
        public int[] Shuffle(int[] values)
        {
            for (var i = values.Length - 1; i > 0; i--) { var j = (int)(Next() % (ulong)(i + 1)); (values[i], values[j]) = (values[j], values[i]); }
            return values;
        }
    }

    private sealed class Network
    {
        public required string[] Ids { get; init; }
        public required double[] Sizes { get; init; }
        public required double[] SelfLoops { get; init; }
        public required List<(int Neighbor, double Weight)>[] Adjacency { get; init; }
        public required List<(int Left, int Right, double Weight)> Edges { get; init; }
        public required int[][] Members { get; init; }
        public int Count => Ids.Length;

        public static Network Create(string[] ids, IReadOnlyList<CommunityProjectionEdge> edges, IReadOnlyDictionary<string, int> index)
        {
            var list = new List<(int Left, int Right, double Weight)>();
            foreach (var edge in edges) if (index.TryGetValue(edge.Source, out var left) && index.TryGetValue(edge.Target, out var right) && left != right)
                list.Add(left < right ? (left, right, edge.Weight) : (right, left, edge.Weight));
            return Build(ids, Enumerable.Repeat(1d, ids.Length).ToArray(), new double[ids.Length], list, Enumerable.Range(0, ids.Length).Select(i => new[] { i }).ToArray());
        }

        public Network Induced(int[] vertices)
        {
            var remap = vertices.Select((old, next) => (old, next)).ToDictionary(item => item.old, item => item.next);
            var edges = Edges.Where(edge => remap.ContainsKey(edge.Left) && remap.ContainsKey(edge.Right))
                .Select(edge => (remap[edge.Left], remap[edge.Right], edge.Weight)).ToList();
            return Build(vertices.Select(i => Ids[i]).ToArray(), vertices.Select(i => Sizes[i]).ToArray(), vertices.Select(i => SelfLoops[i]).ToArray(), edges,
                vertices.Select(i => Members[i].ToArray()).ToArray());
        }

        public Network Aggregate(int[] partition)
        {
            var groups = partition.Select((community, vertex) => (community, vertex)).GroupBy(item => item.community).OrderBy(group => group.Key).ToArray();
            var map = new int[Count]; for (var i = 0; i < groups.Length; i++) foreach (var item in groups[i]) map[item.vertex] = i;
            var sizes = groups.Select(group => group.Sum(item => Sizes[item.vertex])).ToArray();
            var loops = groups.Select(group => group.Sum(item => SelfLoops[item.vertex])).ToArray();
            var combined = new Dictionary<(int, int), double>();
            foreach (var (left, right, weight) in Edges)
            {
                var a = map[left]; var b = map[right];
                if (a == b) loops[a] += weight;
                else { if (a > b) (a, b) = (b, a); combined[(a, b)] = combined.GetValueOrDefault((a, b)) + weight; }
            }
            var edges = combined.OrderBy(item => item.Key.Item1).ThenBy(item => item.Key.Item2).Select(item => (item.Key.Item1, item.Key.Item2, item.Value)).ToList();
            return Build(groups.Select(group => string.Join("+", group.Select(item => Ids[item.vertex]).Order(StringComparer.Ordinal))).ToArray(), sizes, loops, edges,
                groups.Select(group => group.SelectMany(item => Members[item.vertex]).Order().ToArray()).ToArray());
        }

        private static Network Build(string[] ids, double[] sizes, double[] loops, List<(int Left, int Right, double Weight)> edges, int[][] members)
        {
            var adjacency = Enumerable.Range(0, ids.Length).Select(_ => new List<(int, double)>()).ToArray();
            foreach (var (left, right, weight) in edges) { adjacency[left].Add((right, weight)); adjacency[right].Add((left, weight)); }
            foreach (var neighbors in adjacency) neighbors.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return new() { Ids = ids, Sizes = sizes, SelfLoops = loops, Adjacency = adjacency, Edges = edges, Members = members };
        }
    }
}
