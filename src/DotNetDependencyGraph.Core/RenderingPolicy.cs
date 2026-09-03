namespace DotNetDependencyGraph.Core;

public sealed record PhysicsDefaults(
    int Repulsion = 850,
    int LinkDistance = 85,
    double LinkStrength = 0.12,
    int CollisionPadding = 12,
    int DragThreshold = 8,
    double Gravity = 0.035,
    double AlphaDecay = 0.035,
    double AlphaMin = 0.002)
{
    public const string StorageKey = "dotnet-depgraph.physics.v2";

    public bool IsValid() =>
        Repulsion is >= 100 and <= 5000
        && LinkDistance is >= 30 and <= 240
        && LinkStrength is >= 0.02 and <= 1
        && CollisionPadding is >= 2 and <= 50
        && DragThreshold is >= 0 and <= 30
        && Gravity is >= 0 and <= 0.2
        && AlphaDecay is >= 0.01 and <= 0.1
        && AlphaMin is >= 0.0001 and <= 0.02;
}

public static class RenderingPolicy
{
    public const double MinimumNodeDiameter = 18;
    public const double MaximumNodeDiameter = 52;

    public static double NodeDiameter(GraphNode node)
    {
        var importance = Math.Max(0, node.TransitiveDependents) + Math.Max(0, node.InDegree);
        return Math.Clamp(MinimumNodeDiameter + (6 * Math.Log2(importance + 1)), MinimumNodeDiameter, MaximumNodeDiameter);
    }

    public static double CollisionRadius(GraphNode node, double padding) => (NodeDiameter(node) / 2) + Math.Max(0, padding);

    public static bool ShowOverviewLabel(GraphNode node, int rank, int nodeCount, double zoom)
    {
        if (zoom < 0.35) return false;
        if (zoom >= 1.25) return true;
        var limit = Math.Clamp((int)Math.Sqrt(Math.Max(1, nodeCount)), 3, 18);
        return zoom >= 0.7 && rank < limit && node.Centrality > 0;
    }
}
