using System.IO;
using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunCSharpTool
{
    [McpServerTool("run_csharp", "Run C# Script", false, true)]
    [Description("Execute a C# script targeted at this slot's document. The script editor injects `__rhino_doc__` (type `RhinoDoc`) — use it as your document handle instead of `RhinoDoc.ActiveDoc` or anything else. Returns JSON with stdout and error fields; error is null on success.")]
    public static string RunCSharp(
        RhinoDoc doc,
        [Description("Script")] string script)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"-ScriptEditor _Run \"{tmp}\"", false);
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        string[] filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int errIndex = Array.FindIndex(filtered, l =>
            l.StartsWith("Compile Error", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Exception:", StringComparison.Ordinal) ||
            l.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase));

        string stdout;
        string? error;
        if (errIndex >= 0)
        {
            stdout = string.Concat(filtered.Take(errIndex));
            error = string.Concat(filtered.Skip(errIndex));
        }
        else
        {
            stdout = string.Concat(filtered);
            error = null;
        }

        return JsonSerializer.Serialize(new { stdout, error });
    }
}
