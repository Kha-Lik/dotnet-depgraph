using System.Security.Cryptography;
using System.Text;

namespace DotNetDependencyGraph.Core.Domain.Graph;

/// <summary>Produces the stable identity of canonical graph topology.</summary>
public static class GraphFingerprint
{
    public static string Calculate(DependencyGraph graph)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        foreach (var node in graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal)) Add("n|" + node.Id + "|" + node.Kind);
        foreach (var edge in graph.Edges.Where(edge => !edge.Derived).OrderBy(edge => edge.Source, StringComparer.Ordinal).ThenBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.Kind)) Add("e|" + edge.Source + "|" + edge.Target + "|" + edge.Kind);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
