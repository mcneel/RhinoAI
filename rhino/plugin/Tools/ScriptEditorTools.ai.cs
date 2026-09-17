#if R9

using System.IO;
using System.Threading.Tasks;

using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Languages;

namespace Rhino.AI.Tools;

// Rhino's Script Editor as the agent's workspace, ported from ScriptEditorChat. Every tool opens the
// editor when it is closed, so the script stays in front of the user. Panel-only: an external client
// keeps its own permission UI, and for the in-panel agent the Off / On / Ask modes are the safety switch.
[McpServerToolType]
[ToolGroup("Script Editor")]
internal static class ScriptEditorTools
{
    private const string NewTitle = "assistant.py";

    [McpServerTool("script_editor_read", "Read Script", readOnly: true)]
    [InPanelOnly]
    [Description("Read the document currently open in Rhino's Script Editor: title, file path, language, line count and full text. Call this before editing, because the user may have changed the script since your last turn. Opens the editor if it is closed.")]
    public static async Task<IToolResult> Read(RhinoDoc doc)
    {
        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();

        if (session.Current() is not Code code)
            return Success(
                new { open = false, lineCount = 0, text = string.Empty },
                "The editor has no document; script_editor_edit_lines with startLine 1 creates one.");

        string text = ScriptEditorSession.Text(code);
        return Success(new
        {
            title = code.Title,
            path = ScriptEditorSession.Path(code),
            language = ScriptEditorSession.Language(code),
            lineCount = Split(text, out _).Length,
            text,
        });
    }

    [McpServerTool("script_editor_read_lines", "Read Script Lines", readOnly: true)]
    [InPanelOnly]
    [Description("Read a range of lines from the current Script Editor document, each prefixed with its 1-based line number. Opens the editor if it is closed.")]
    public static async Task<IToolResult> ReadLines(
        RhinoDoc doc,
        [Description("First line (1-based)")] int startLine,
        [Description("Last line, inclusive; 0 means through the last line")] int endLine = 0)
    {
        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();
        if (session.Current() is not Code code)
            return NoDocument();

        string[] lines = Split(ScriptEditorSession.Text(code), out _);
        if (lines.Length == 0)
            return Success(new { startLine = 1, endLine = 0, lineCount = 0, lines = Array.Empty<string>() }, "The document is empty.");
        int last = endLine == 0 ? lines.Length : endLine;

        Refusals refusals = new();
        if (startLine < 1 || startLine > lines.Length)
            refusals.Add($"startLine {startLine} is outside 1..{lines.Length}", "call script_editor_read to see the line count");
        if (last < startLine || last > lines.Length)
            refusals.Add($"endLine {endLine} must be between startLine and {lines.Length}", "0 reads through the last line");
        if (refusals.Any)
            return refusals.Rejection;

        return Success(new { startLine, endLine = last, lineCount = lines.Length, lines = Numbered(lines, startLine, last) });
    }

    [McpServerTool("script_editor_edit_lines", "Modify Script Lines")]
    [InPanelOnly]
    [Description("Replace, insert or delete lines in the current Script Editor document. Lines are 1-based and endLine is inclusive. Replace: lines startLine..endLine become newText. Insert: endLine = startLine - 1 puts newText before startLine (startLine = lineCount + 1 appends). Delete: newText empty. Rewrite everything with startLine 1 and endLine 0 (through the last line). With no document open, a new one is created (startLine must be 1). Returns which lines changed plus the surrounding text; tell the user which lines you edited.")]
    public static async Task<IToolResult> EditLines(
        RhinoDoc doc,
        [Description("First line to change (1-based); lineCount + 1 appends")] int startLine,
        [Description("Last line to change, inclusive; startLine - 1 inserts before startLine; 0 means through the last line")] int endLine,
        [Description("Replacement text; empty deletes the range")] string newText,
        [Description("Language of a NEW document only: python (default) or csharp")] string language = "python")
    {
        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();

        Code? code = session.Current();
        string current = code is null ? string.Empty : ScriptEditorSession.Text(code);
        string[] lines = Split(current, out bool trailingNewline);
        int last = endLine == 0 ? lines.Length : endLine;

        Refusals refusals = new();
        if (startLine < 1 || startLine > lines.Length + 1)
            refusals.Add($"startLine {startLine} is outside 1..{lines.Length + 1}", "call script_editor_read first");
        if (last < startLine - 1 || last > lines.Length)
            refusals.Add($"endLine {endLine} must be between {startLine - 1} and {lines.Length}", "0 means through the last line");
        if (refusals.Any)
            return refusals.Rejection;

        string[] replacement = Split(newText, out _);
        string[] result = [.. lines[..(startLine - 1)], .. replacement, .. lines[last..]];
        string text = Join(result, trailingNewline || code is null);

        if (code is null)
        {
            LanguageSpec spec = SpecFor(language);
            code = await session.AddAsync(spec, text, spec == LanguageSpec.CSharp ? "assistant.cs" : NewTitle);
            if (code is null)
                return Failure(ToolError.Failed, "Could not create a document in the Script Editor.");
        }
        else
        {
            ScriptEditorSession.SetText(code, text);
        }

        int replacedCount = last - startLine + 1;
        string edited = replacement.Length == 0
            ? replacedCount == 1 ? $"removed line {startLine}" : $"removed lines {startLine}–{last}"
            : replacement.Length == 1 ? $"line {startLine}" : $"lines {startLine}–{startLine + replacement.Length - 1}";

        int from = Math.Max(1, startLine - 3);
        int to = Math.Min(result.Length, startLine + replacement.Length + 2);
        return Success(new
        {
            edited,
            lineCount = result.Length,
            context = to >= from ? Numbered(result, from, to) : [],
        });
    }

    [McpServerTool("script_editor_clear", "Clear Script", destructive: true)]
    [InPanelOnly]
    [Description("Empty the current Script Editor document; the tab stays open. Use it only when the user asks for a fresh start.")]
    public static async Task<IToolResult> Clear(RhinoDoc doc)
    {
        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();
        if (session.Current() is not Code code)
            return Success(new { cleared = false }, "The editor has no document, so there was nothing to clear.");

        ScriptEditorSession.SetText(code, string.Empty);
        return Success(new { cleared = true, title = code.Title });
    }

    [McpServerTool("script_editor_load", "Load Script", enabledByDefault: false)]
    [InPanelOnly]
    [Description("Open a script file (.py or .cs) from disk as a new Script Editor document and make it current. Off by default; the user turns it on in Rhino AI settings.")]
    public static async Task<IToolResult> Load(
        RhinoDoc doc,
        [Description("Absolute path of the script file")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Failure(ToolError.BadArgument, "path is required.");
        path = path.Trim();
        if (!Path.IsPathRooted(path))
            return Failure(ToolError.BadArgument, $"path must be absolute: {path}");
        if (!File.Exists(path))
            return Failure(ToolError.RH_File_NotFound, $"File not found: {path}");

        LanguageSpec? spec = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".py" => LanguageSpec.Python3,
            ".cs" => LanguageSpec.CSharp,
            _ => null,
        };
        if (spec is null)
            return Failure(ToolError.Unsupported, $"Only .py and .cs files can be loaded: {path}");

        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();
        if (await session.LoadAsync(spec, path) is not Code code)
            return Failure(ToolError.Failed, $"The Script Editor could not open {path}.");

        return Success(new
        {
            title = code.Title,
            path = ScriptEditorSession.Path(code) ?? path,
            lineCount = Split(ScriptEditorSession.Text(code), out _).Length,
        });
    }

    [McpServerTool("script_editor_save", "Save Script", enabledByDefault: false)]
    [InPanelOnly]
    [Description("Save the current Script Editor document to its own file, or to `path` (absolute; behaves like Save As). Off by default; the user turns it on in Rhino AI settings.")]
    public static async Task<IToolResult> Save(
        RhinoDoc doc,
        [Description("Optional absolute path to save to; omit to save to the document's own file")] string? path = null)
    {
        if (path is not null)
        {
            path = path.Trim();
            if (path.Length == 0)
                path = null;
            else if (!Path.IsPathRooted(path))
                return Failure(ToolError.BadArgument, $"path must be absolute: {path}");
        }

        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();
        if (session.Current() is not Code code)
            return NoDocument();
        if (path is null && !code.HasStorage)
            return Failure(ToolError.BadArgument, "The document has never been saved.", "Pass an absolute path to save it to.");

        if (!ScriptEditorSession.Save(code, path, out string location, out bool bound))
            return Failure(ToolError.RH_Write_Failed, $"Failed to save: {location}");

        return Success(
            new { path = location, title = code.Title },
            bound ? null : "The file was written, but the editor tab still points at its previous file.");
    }

    [McpServerTool("script_editor_run", "Run Current Script Editor Script", destructive: true, confirmByDefault: true)]
    [InPanelOnly]
    [Description("Run the document currently open in Rhino's Script Editor against this Rhino document and return its stdout, stderr and error. This runs the USER'S script, whatever it happens to be — it is not a way to run code you wrote, which is what run_python is for. Rhino asks the user to approve every run; if they decline, do not retry on your own. On an error, fix the script with script_editor_edit_lines and run again.")]
    public static async Task<IToolResult> Run(RhinoDoc doc)
    {
        if (await ScriptEditorSession.OpenAsync() is not ScriptEditorSession session)
            return NoEditor();
        if (session.Current() is not Code code)
            return NoDocument();

        ScriptRunOutcome outcome = ScriptEditorSession.Run(doc, code);
        object payload = new { title = code.Title, stdout = outcome.Stdout, stderr = outcome.Stderr };

        if (!outcome.Success)
            return Failure(
                ToolError.Failed,
                ContentBlock.CreateJson(payload),
                string.IsNullOrWhiteSpace(outcome.Error) ? outcome.Stderr.TrimEnd() : outcome.Error);

        return Success(payload, outcome.Stderr.Length > 0 ? "The script wrote to stderr but ran to completion" : null);
    }

    // What the permission card shows instead of the bare arguments. A run's arguments say nothing
    // about what will happen, so it answers the question the user is actually asking: what is this
    // script, and will it stop to ask me for something? Read on the UI thread by ToolPolicy.
    public static string? DescribeCall(string name, IDictionary<string, JsonElement>? arguments)
    {
        if (!string.Equals(name, "script_editor_run", StringComparison.Ordinal))
            return null;

        if (!ScriptEditorSession.TryGetCurrentText(out string title, out string text))
            return null;

        string[] lines = Split(text, out _);
        string header = $"{(title.Length > 0 ? title : "Untitled")} — {lines.Length} line{(lines.Length == 1 ? string.Empty : "s")}";

        if (ScriptInputs.Describe(text) is { } waits)
            header += "\n" + waits;

        return text.Length == 0 ? header : header + "\n\n" + text;
    }

    private static IToolResult NoEditor() =>
        Failure(ToolError.Failed, "The Script Editor did not open.", "Ask the user to run the ScriptEditor command, then retry.");

    private static IToolResult NoDocument() =>
        Failure(ToolError.NotFound, "The Script Editor has no document open.", "script_editor_edit_lines with startLine 1 creates one.");

    private static LanguageSpec SpecFor(string language) =>
        language.Trim().ToLowerInvariant() is "csharp" or "c#" or "cs" ? LanguageSpec.CSharp : LanguageSpec.Python3;

    // One trailing newline is a file convention, not a line: it is stripped here and restored by Join.
    private static string[] Split(string text, out bool trailingNewline)
    {
        string normalized = (text ?? string.Empty).Replace("\r\n", "\n");
        trailingNewline = normalized.EndsWith('\n');
        if (trailingNewline)
            normalized = normalized[..^1];
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }

    private static string Join(string[] lines, bool trailingNewline) =>
        lines.Length == 0 ? string.Empty : string.Join("\n", lines) + (trailingNewline ? "\n" : string.Empty);

    private static string[] Numbered(string[] lines, int from, int to)
    {
        string[] numbered = new string[to - from + 1];
        for (int i = from; i <= to; i++)
            numbered[i - from] = $"{i}: {lines[i - 1]}";
        return numbered;
    }
}

#endif
