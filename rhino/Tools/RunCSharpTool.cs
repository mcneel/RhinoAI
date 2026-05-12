using System.IO;
using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunCSharpTool
{
    [McpServerTool(Name = "run_csharp")]
    [Description("Execute a C# script. Returns JSON with stdout and error fields; error is null on success.")]
    public static string RunCSharp(
        [Description("Script")] string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.InvokeAndWait(() => RhinoApp.RunScript($"-ScriptEditor _Run \"{tmp}\"", false));
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        var filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int errIndex = Array.FindIndex(filtered, l =>
            l.Contains("error CS", StringComparison.Ordinal) ||
            l.Contains("Exception:", StringComparison.Ordinal) ||
            l.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase));

        string stdout;
        string error;
        if (errIndex >= 0)
        {
            stdout = string.Join("\n", filtered.Take(errIndex));
            error = string.Join("\n", filtered.Skip(errIndex));
        }
        else
        {
            stdout = string.Join("\n", filtered);
            error = string.Empty;
        }

        return JsonSerializer.Serialize(new { stdout, error });
    }
}
