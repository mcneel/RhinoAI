using Rhino.AI.ScriptProjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class RunPythonTool
{
    [McpServerTool("run_python", "Run Python Script", false, true, confirmByDefault: true)]
    [Description("Execute a Python 3 script against this slot's document and return its stdout, stderr "
        + "and error. Import what you need — Rhino's own API, the standard library, anything installed. "
        + "The script editor injects `__rhino_doc__` — use it as your document handle. Do NOT trust "
        + "`scriptcontext.doc` or `rhinoscriptsyntax` calls to reach the right document. Rhino asks the "
        + "user to approve every call and shows them the script, so write it to be read: if it touches "
        + "anything outside the model — the file system, the network, another process — say so in your "
        + "message before you ask.")]
    public static IToolResult RunPython(
        RhinoDoc doc,
        [Description("Script")] string script)
        => ScriptProjectRunner.RunScript(doc, Lang.Python3, script);

    // The permission card shows the script instead of the arguments, because here the script IS the
    // argument and reading it is the whole point of being asked.
    public static string? DescribeCall(string name, IDictionary<string, JsonElement>? arguments)
    {
        if (!string.Equals(name, "run_python", StringComparison.Ordinal))
            return null;
        if (arguments is null || !arguments.TryGetValue("script", out JsonElement script))
            return null;
        if (script.ValueKind != JsonValueKind.String || script.GetString() is not { Length: > 0 } text)
            return null;

        int lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
        string header = $"Python — {lines} line{(lines == 1 ? string.Empty : "s")}";
        if (ScriptInputs.Describe(text) is { } waits)
            header += "\n" + waits;
        return header + "\n\n" + text;
    }
}
