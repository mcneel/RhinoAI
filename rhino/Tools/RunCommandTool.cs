namespace RhMcp.Tools;

[McpServerToolType]
public static class RunCommandTool
{
    [McpServerTool(Name = "run_command")]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10\"")]
    public static string RunCommand(
        [Description("Rhino command string to execute")] string command)
    {
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.InvokeAndWait(() => RhinoApp.RunScript(command, false));
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        return lines is { Length: > 0 } ? string.Join("\n", lines) : "Done.";
    }
}
