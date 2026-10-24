using Rhino.AI.ScriptProjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class RunCSharpTool
{

    private const string HEADER_NOTATION = "// #! csharp";

    [McpServerTool("run_csharp", "Run C# Script", false, true)]
    [Description("Execute a C# script targeted at this slot's document. The script editor injects `__rhino_doc__` (type `RhinoDoc`) — use it as your document handle instead of `RhinoDoc.ActiveDoc` or anything else. System/Linq/Collections.Generic and Rhino/Rhino.Geometry/Rhino.DocObjects are auto-injected if missing. Use `Console.WriteLine` for stdout (`RhinoApp.WriteLine` is not captured in stdout; never throw an exception to return output).")]
    public static IToolResult RunCSharp(
        RhinoDoc doc,
        [Description("Script")] string script)
    {
        script = InjectUsings(script);

        return ScriptProjectRunner.RunScript(doc, Lang.CSharp, script);
    }

    private static string[] Usings { get; } = [
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "Rhino",
        "Rhino.Geometry",
        "Rhino.DocObjects",
    ];

    public static string InjectUsings(string script)
    {
        foreach (string @using in Usings)
        {
            string usingString = $"using {@using};";
            if (script.Contains(usingString))
                continue;

            int index = script.IndexOf(HEADER_NOTATION);
            if (index >= 0)
            {
                index += HEADER_NOTATION.Length;
                script = script.Insert(index, "\n");
                index++;
            }
            else
            {
                index = 0;
            }

            script = script.Insert(index, usingString + "\n");
        }

        return script;  
    }

}
