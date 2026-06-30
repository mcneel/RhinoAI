using Rhino.Commands;

namespace RhMcp.Tools;

[McpServerToolType]
public static class RunCommandTool
{
    [McpServerTool("run_command", "Run Rhino Command", false, true)]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10 _Enter\"")]
    public static string RunCommand(
        RhinoDoc doc,
        [Description("Rhino command string to execute")] string command)
    {
        if (Command.InCommand())
        {
            return "Rhino is already running a command (likely waiting for input from a previous run_command call). " +
                   "Call close_slot to kill this slot and start a new one, then use run_python or run_csharp for scripted geometry.";
        }

        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, command, false);
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        return lines is { Length: > 0 } ? string.Concat(lines) : "Done.";
    }
}
