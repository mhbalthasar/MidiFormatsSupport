using System.Globalization;

namespace DawCore.Parsers;

/// A REAPER .rpp project is a plain-text chunk tree: lines are either "TOKEN args", an opening "<TOKEN args" or a lone ">".
public sealed class RppChunk
{
    public string Name { get; }
    public IReadOnlyList<string> Args { get; }

    private readonly List<RppChunk> _children = new();
    public IReadOnlyList<RppChunk> Children => _children;

    public RppChunk(string name, IReadOnlyList<string> args)
    {
        Name = name;
        Args = args;
    }

    internal void Add(RppChunk child) => _children.Add(child);

    /// The n-th argument, or "" when absent.
    public string Arg(int index) => index >= 0 && index < Args.Count ? Args[index] : "";

    /// First direct child with this token, or null.
    public RppChunk? Child(string name) => _children.FirstOrDefault(c => c.Name == name);

    /// All direct children with this token.
    public IEnumerable<RppChunk> ChildrenNamed(string name) => _children.Where(c => c.Name == name);

    /// First argument of a same-named child (e.g. NAME "Drums"), or null.
    public string? Value(string name) => Child(name) is { Args.Count: > 0 } c ? c.Args[0] : null;

    /// Numeric first argument of a same-named child.
    public double Number(string name, double fallback = 0)
        => Child(name) is { Args.Count: > 0 } c
           && double.TryParse(c.Args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
               ? v
               : fallback;

    /// Numeric n-th argument of this chunk.
    public double Number(int index, double fallback = 0)
        => index >= 0 && index < Args.Count
           && double.TryParse(Args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
               ? v
               : fallback;

    /// Parses a whole .rpp document; unknown/malformed lines are skipped rather than thrown on.
    public static RppChunk Parse(string text)
    {
        var root = new RppChunk("ROOT", Array.Empty<string>());
        var stack = new Stack<RppChunk>();
        stack.Push(root);

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line == ">")
            {
                if (stack.Count > 1) stack.Pop();
                continue;
            }

            bool opening = line[0] == '<';
            var (token, args) = Split(opening ? line[1..] : line);
            if (token.Length == 0) continue;

            var chunk = new RppChunk(token, args);
            stack.Peek().Add(chunk);
            if (opening) stack.Push(chunk);
        }
        return root;
    }

    /// Splits a line into a token and its arguments, honouring double-quoted fields.
    private static (string, List<string>) Split(string s)
    {
        int i = 0;
        var parts = new List<string>();
        while (i < s.Length)
        {
            while (i < s.Length && s[i] == ' ') i++;
            if (i >= s.Length) break;

            if (s[i] == '"')
            {
                int close = s.IndexOf('"', i + 1);
                if (close < 0) close = s.Length;
                parts.Add(s[(i + 1)..close]);
                i = close + 1;
            }
            else
            {
                int j = i;
                while (j < s.Length && s[j] != ' ') j++;
                parts.Add(s[i..j]);
                i = j;
            }
        }
        return parts.Count == 0 ? ("", new List<string>()) : (parts[0], parts.Skip(1).ToList());
    }
}
