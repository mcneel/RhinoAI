using System.Diagnostics;
using Eto.Forms;
using Rhino.Commands;
using Rhino.Input.Custom;
using Rhino.UI;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class RunCommandTool
{
    private const long PromptQuietMs = 1000;
    private const long CancelRetryMs = 1000;

    [McpServerTool("run_command", "Run Rhino Command", false, true)]
    [Description("Execute any Rhino command string and return command window output. Example: \"_Box 0,0,0 10,10,10 _Enter\"")]
    public static IToolResult RunCommand(
        RhinoDoc doc,
        [Description("Rhino command string to execute")] string command)
    {
        // TODO : RhinoApp.CommandWindowCaptureEnabled is not document specific

        Stopwatch clock = Stopwatch.StartNew();
        PromptSnapshot? latestPrompt = null;
        long lastPromptMs = 0;
        PromptSnapshot? stalledAt = null;
        long cancelSentMs = 0;

        void OnPromptChanged(object? sender, CommandPromptChangedEventArgs e)
        {
            latestPrompt = new PromptSnapshot(
                e.Prompt,
                e.PromptDefault,
                (e.Options ?? []).Where(o => o.OptionType != CommandLineOptionType.Hidden).Select(o => o.EnglishName).ToArray());
            lastPromptMs = clock.ElapsedMilliseconds;
        }

        // Idle never fires while a command waits for input and PostCancelEvent only lands once the get loop wakes, so poll with a UI timer and cancel by keystroke.
        UITimer stallWatch = new() { Interval = 0.25 };
        stallWatch.Elapsed += (_, _) =>
        {
            long now = clock.ElapsedMilliseconds;
            if (stalledAt is null)
            {
                if (latestPrompt is null || now - lastPromptMs < PromptQuietMs)
                    return;
                stalledAt = latestPrompt;
            }
            else if (now - cancelSentMs < CancelRetryMs)
                return;

            cancelSentMs = now;
            RhinoApp.SendKeystrokes("!", true);
        };

        RhinoApp.CommandPromptChanged += OnPromptChanged;
        RhinoApp.CommandWindowCaptureEnabled = true;
        stallWatch.Start();

        bool ran;
        string[] lines;
        try
        {
            ran = RhinoApp.RunScript(doc.RuntimeSerialNumber, command, false);
        }
        finally
        {
            stallWatch.Stop();
            stallWatch.Dispose();
            RhinoApp.CommandPromptChanged -= OnPromptChanged;
            lines = RhinoApp.CapturedCommandWindowStrings(true);
            RhinoApp.CommandWindowCaptureEnabled = false;
        }

        ContentBlock output = ContentBlock.CreateText(lines is { Length: > 0 } ? string.Concat(lines) : "Done.");

        if (stalledAt is not null && !ran)
        {
            return Failure(
                ToolError.BadArgument,
                [ContentBlock.CreateJson(stalledAt), output],
                $"'{command}' stopped at a prompt waiting for input and was cancelled",
                "Call run_command again with a command string that answers this prompt, e.g. a value, an option name, or _Enter to accept the default");
        }

        return ran
            ? Success(output)
            : Failure(ToolError.Failed, output, $"Rhino did not run '{command}' to completion");
    }

    private sealed record PromptSnapshot(string? Prompt, string? PromptDefault, string[] Options);
}
