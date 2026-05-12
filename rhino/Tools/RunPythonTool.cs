using System.IO;
using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunPythonTool
{
    [McpServerTool(Name = "run_python")]
    [Description("Execute a Python 3 script. Returns JSON with stdout and error fields; error is null on success.")]
    public static string RunPython(
        [Description("Script")] string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.py");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.InvokeAndWait(() => RhinoApp.RunScript($"-ScriptEditor _Run \"{tmp}\"", false));
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        // Saves a few tokens
        var filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int tbIndex = Array.FindIndex(filtered, l => l.Contains("Traceback (most recent call last):"));
        
        string stdout;
        string error;
        if (tbIndex >= 0)
        {
            stdout = string.Join("\n", filtered.Take(tbIndex));
            error = string.Join("\n", filtered.Skip(tbIndex));
        }
        else
        {
            stdout = string.Join("\n", filtered);
            error = string.Empty;
        }

        return JsonSerializer.Serialize(new { stdout, error });
    }
}
