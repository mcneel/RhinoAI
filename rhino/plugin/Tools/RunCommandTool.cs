using Rhino.Commands;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class RunCommandTool
{
    // RhinoDoc.ActiveDoc and the command-window capture are process-wide, and a headless host
    // runs several session documents in one process, so one command at a time.
    private static readonly object s_commandLock = new();

    [McpServerTool("run_command", "Run Rhino Command", false, true)]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10 _Enter\"")]
    public static IToolResult RunCommand(
        RhinoDoc doc,
        [Description("Rhino command string to execute")] string command)
    {
        lock (s_commandLock)
        {
            // RunScript takes the document as the command context, but commands that pick or
            // act on selected objects (Move, Delete, ...) read the ACTIVE document. Headless
            // documents (Rhino.Compute session documents) are never the active one, so such
            // commands silently worked on the wrong document. Make the target active for the
            // duration of the command and put the previous one back afterwards.
            // Only for headless documents: on desktop Rhino the listener's document is already
            // active on Windows, and on the Mac making a slot document active would pull the
            // user's window focus on every command.
            RhinoDoc? previous = RhinoDoc.ActiveDoc;
            bool switched = doc.IsHeadless
                && (previous is null || previous.RuntimeSerialNumber != doc.RuntimeSerialNumber);
            if (switched)
                RhinoDoc.ActiveDoc = doc;

            bool ran;
            string[] lines;
            try
            {
                RhinoApp.CommandWindowCaptureEnabled = true;
                ran = RhinoApp.RunScript(doc.RuntimeSerialNumber, command, false);
                lines = RhinoApp.CapturedCommandWindowStrings(true);
            }
            finally
            {
                RhinoApp.CommandWindowCaptureEnabled = false;
                if (switched && previous is not null && RhinoDoc.FromRuntimeSerialNumber(previous.RuntimeSerialNumber) is not null)
                    RhinoDoc.ActiveDoc = previous;
            }

            ContentBlock output = ContentBlock.CreateText(lines is { Length: > 0 } ? string.Concat(lines) : "Done.");

            return ran
                ? Success(output)
                : Failure(ToolError.Failed, output, $"Rhino did not run '{command}' to completion");
        }
    }
}
