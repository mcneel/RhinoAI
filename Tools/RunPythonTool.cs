using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Rhino;

namespace RhMcp.Tools;

public sealed class RunPythonTool : IMcpTool
{
    public string Name => "run_python";
    public string Description => "Execute a Python 3 script in the Rhino Script Editor and return command window output.";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            script = new { type = "string", description = "Python 3 code to execute" }
        },
        required = new[] { "script" }
    };

    public object Execute(JsonObject? args)
    {
        var script = args?["script"]?.GetValue<string>() ?? "";
        var tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.py");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript($"-ScriptEditor _Run \"{tmp}\"", false);
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });
        var output = lines is { Length: > 0 } ? string.Join("\n", lines) : "Done.";
        return new { content = new[] { new { type = "text", text = output } } };
    }
}
