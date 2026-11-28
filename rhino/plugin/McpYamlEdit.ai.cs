using System.Text.RegularExpressions;

namespace Rhino.AI;

// Edits lines rather than a parsed tree so the user's YAML comments survive; null means a shape it cannot edit safely.
internal static class McpYamlEdit
{
    private const string Step = "  ";

    private enum Shape { Block, EmptyFlow, Other }

    private readonly record struct Container(int Header, int End, Shape Shape);

    public static bool HasEntry(string content, string containerKey, string serverName)
    {
        List<string> lines = Lines(content);
        return FindContainer(lines, containerKey) is { Shape: Shape.Block } container
            && FindEntry(lines, container, serverName) is not null;
    }

    public static string? Add(string original, string containerKey, string serverName, string command, IReadOnlyDictionary<string, string> env)
    {
        List<string> lines = Lines(original);
        switch (FindContainer(lines, containerKey))
        {
            case null:
                if (lines.Count > 0 && !IsBlank(lines[lines.Count - 1]))
                    lines.Add(string.Empty);
                lines.Add($"{containerKey}:");
                lines.AddRange(Entry(Step, serverName, command, env));
                break;

            case { Shape: Shape.Block } container when FindEntry(lines, container, serverName) is not null:
                return null;

            case { Shape: Shape.Block } container:
                string indent = new(' ', ChildIndent(lines, container) ?? Step.Length);
                lines.InsertRange(container.Header + 1, Entry(indent, serverName, command, env));
                break;

            case { Shape: Shape.EmptyFlow } container:
                lines[container.Header] = $"{containerKey}:";
                lines.InsertRange(container.Header + 1, Entry(Step, serverName, command, env));
                break;

            case { Shape: Shape.Other }:
                return null;
        }

        return Join(lines, original);
    }

    public static string? Remove(string original, string containerKey, string serverName)
    {
        List<string> lines = Lines(original);
        if (FindContainer(lines, containerKey) is not { Shape: Shape.Block } container)
            return null;
        if (FindEntry(lines, container, serverName) is not int start)
            return original;

        int indent = Indent(lines[start]);
        int last = start;
        for (int i = start + 1; i < container.End; i++)
        {
            if (IsBlank(lines[i]) || IsComment(lines[i]))
                continue;
            if (Indent(lines[i]) <= indent)
                break;
            last = i;
        }

        int stop = last + 1;
        if (IsBlank(lines[start - 1]))
        {
            while (stop < container.End && IsBlank(lines[stop]))
                stop++;
        }

        int removed = stop - start;
        lines.RemoveRange(start, removed);

        bool isNowNull = ChildIndent(lines, container with { End = container.End - removed }) is null;
        if (isNowNull)
            lines[container.Header] = $"{containerKey}: {{}}";

        return Join(lines, original);
    }

    private static Container? FindContainer(List<string> lines, string containerKey)
    {
        Regex header = new("^([\"']?)" + Regex.Escape(containerKey) + @"\1[ \t]*:(?<rest>.*)$");
        for (int i = 0; i < lines.Count; i++)
        {
            Match match = header.Match(lines[i]);
            if (!match.Success)
                continue;

            string value = TrailingComment.Replace(match.Groups["rest"].Value, string.Empty).Trim();
            if (value is "{}" or "null" or "~")
                return new Container(i, i + 1, Shape.EmptyFlow);
            if (value.Length > 0)
                return new Container(i, i + 1, Shape.Other);

            Container block = new(i, BlockEnd(lines, i), Shape.Block);
            return FirstChild(lines, block) is int child && IsSequenceItem(lines[child].TrimStart())
                ? block with { Shape = Shape.Other }
                : block;
        }

        return null;
    }

    private static int BlockEnd(List<string> lines, int header)
    {
        int end = header + 1;
        while (end < lines.Count && !StartsTopLevelKey(lines[end]))
            end++;
        return end;
    }

    private static int? FindEntry(List<string> lines, Container container, string serverName)
    {
        if (ChildIndent(lines, container) is not int indent)
            return null;

        Regex key = new("^ {" + indent + "}([\"']?)" + Regex.Escape(serverName) + @"\1[ \t]*:");
        for (int i = container.Header + 1; i < container.End; i++)
        {
            if (key.IsMatch(lines[i]))
                return i;
        }

        return null;
    }

    private static int? ChildIndent(List<string> lines, Container container)
        => FirstChild(lines, container) is int child ? Indent(lines[child]) : null;

    private static int? FirstChild(List<string> lines, Container container)
    {
        for (int i = container.Header + 1; i < container.End; i++)
        {
            if (!IsBlank(lines[i]) && !IsComment(lines[i]))
                return i;
        }

        return null;
    }

    private static List<string> Entry(string indent, string serverName, string command, IReadOnlyDictionary<string, string> env)
    {
        List<string> entry =
        [
            $"{indent}{serverName}:",
            $"{indent}{Step}command: {Quoted(command)}",
            $"{indent}{Step}args: []",
        ];

        if (env.Count > 0)
        {
            entry.Add($"{indent}{Step}env:");
            foreach (KeyValuePair<string, string> pair in env)
                entry.Add($"{indent}{Step}{Step}{pair.Key}: {Quoted(pair.Value)}");
        }

        return entry;
    }

    private static List<string> Lines(string content)
    {
        List<string> lines = [.. content.Replace("\r\n", "\n").Split('\n')];
        if (lines[lines.Count - 1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string Join(List<string> lines, string original)
    {
        string newline = original.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(newline, lines) + newline;
    }

    private static bool StartsTopLevelKey(string line)
        => line.Length > 0 && line[0] is not (' ' or '\t' or '#') && !IsSequenceItem(line);

    private static bool IsSequenceItem(string text) => text == "-" || text.StartsWith("- ", StringComparison.Ordinal);

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static bool IsComment(string line) => line.TrimStart().StartsWith("#", StringComparison.Ordinal);

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static string Quoted(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static Regex TrailingComment { get; } = new(@"(^|\s)#.*$");
}
