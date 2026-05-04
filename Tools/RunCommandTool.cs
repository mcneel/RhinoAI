using System.Text.Json.Nodes;
using Rhino;

namespace RhMcp.Tools;

public sealed class RunCommandTool : IMcpTool
{
    public string Name => "run_command";
    public string Description => "Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10\"";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "Rhino command string to execute" }
        },
        required = new[] { "command" }
    };

    public object Execute(JsonObject? args)
    {
        var command = args?["command"]?.GetValue<string>() ?? "";
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(command, false);
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        var output = lines is { Length: > 0 } ? string.Join("\n", lines) : "Done.";
        return new { content = new[] { new { type = "text", text = output } } };
    }
}
