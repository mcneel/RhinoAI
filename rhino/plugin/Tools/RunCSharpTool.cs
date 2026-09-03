namespace RhinoAI.Tools;

[McpServerToolType]
public static class RunCSharpTool
{
    [McpServerTool("run_csharp", "Run C# Script", false, true)]
    [Description("Execute a C# script targeted at this slot's document. The script editor injects `__rhino_doc__` (type `RhinoDoc`) — use it as your document handle instead of `RhinoDoc.ActiveDoc` or anything else")]
    public static string RunCSharp(
        RhinoDoc doc,
        [Description("Script")] string script)
        => RunScriptToolBase.RunScript(doc, RunScriptToolBase.Lang.CSharp, script);

}
