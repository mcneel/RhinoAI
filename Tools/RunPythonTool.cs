using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

using ModelContextProtocol.Server;

using Rhino;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunPythonTool
{
    [McpServerTool(Name = "run_python")]
    [Description("Execute a Python 3 script in the Rhino Script Editor and return command window output.")]
    public static string RunPython(
        [Description("Python 3 code to execute")] string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.py");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript($"-ScriptEditor _Run \"{tmp}\"", false);
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });
        return lines is { Length: > 0 } ? string.Join("\n", lines) : "Done.";
    }
}
