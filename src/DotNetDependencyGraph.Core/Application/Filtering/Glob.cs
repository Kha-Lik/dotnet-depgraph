namespace DotNetDependencyGraph.Core.Application.Filtering;

public static class Glob
{
    public static bool IsMatch(string value, string pattern, bool ignoreCase = true)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var v = value.Replace('\\', '/');
        var p = pattern.Replace('\\', '/');
        var rows = new bool[p.Length + 1, v.Length + 1];
        rows[0, 0] = true;
        for (var i = 1; i <= p.Length; i++)
            if (p[i - 1] == '*') rows[i, 0] = rows[i - 1, 0];
        for (var i = 1; i <= p.Length; i++)
            for (var j = 1; j <= v.Length; j++)
            {
                var pc = p[i - 1];
                rows[i, j] = pc == '*'
                    ? rows[i - 1, j] || rows[i, j - 1]
                    : (pc == '?' || string.Equals(pc.ToString(), v[j - 1].ToString(), comparison)) && rows[i - 1, j - 1];
            }
        return rows[p.Length, v.Length];
    }
}
