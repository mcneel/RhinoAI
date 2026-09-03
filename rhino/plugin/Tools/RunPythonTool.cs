using RhinoAI.ScriptProjects;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class RunPythonTool
{
    [McpServerTool("run_python", "Run Python Script", false, true)]
    [Description("Execute a Python 3 script targeted at this slot's document. The script editor injects `__rhino_doc__` — use it as your document handle. Do NOT trust `scriptcontext.doc` or `rhinoscriptsyntax` calls.")]
    public static string RunPython(
        RhinoDoc doc,
        [Description("Script")] string script)
        => ScriptProjectRunner.RunScript(doc, Lang.Python3, script);
}
