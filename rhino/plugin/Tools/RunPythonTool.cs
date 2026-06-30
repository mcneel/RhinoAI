using System.IO;
using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunPythonTool
{
    [McpServerTool("run_python", "Run Python Script", false, true)]
    [Description("Execute a Python 3 script targeted at this slot's document. The script editor injects `__rhino_doc__` — use it as your document handle. Do NOT trust `scriptcontext.doc` or `rhinoscriptsyntax` calls. Returns JSON with stdout and error fields; error is null on success.")]
    public static string RunPython(
        RhinoDoc doc,
        [Description("Script")] string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.py");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"-ScriptEditor _Run \"{tmp}\"", false);
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        var filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int errIndex = Array.FindIndex(filtered, l =>
            l.StartsWith("Compile Error", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Traceback (most recent call last):", StringComparison.Ordinal));

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
