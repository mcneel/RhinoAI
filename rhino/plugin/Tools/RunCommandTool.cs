namespace RhMcp.Tools;

[McpServerToolType]
public static class RunCommandTool
{
    [McpServerTool(Name = "run_command")]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10\"")]
    public static string RunCommand(
        RhinoDoc doc,
        [Description("Rhino command string to execute")] string command)
    {
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, command, false);
        var lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;
        return lines is { Length: > 0 } ? string.Join("\n", lines) : "Done.";
    }
}
