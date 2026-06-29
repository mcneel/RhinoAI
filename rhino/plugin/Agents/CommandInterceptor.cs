namespace RhMcp;

/// <summary>
/// Routes command-line entries that start with <c>"</c> to this document's agent. Rhino rejects
/// <c>"</c> as a command name and exposes no pre-parse hook, so we read the command history *after*
/// the parser handles the entry: a leading-<c>"</c> entry only ever shows up as a <c>Command: "..."</c>
/// line for real command-line submissions, never from other text inputs.
/// </summary>
internal sealed class CommandInterceptor : IDisposable
{
    private const char Sigil = '"';
    private const string RoutedMarker = "[claude]";

    // TODO : Handle different locales
    private const string CommandPrefix = "Command: ";

    private RhinoDoc Doc { get; }
    private int HistoryCursor { get; set; }
    private bool Active { get; set; }

    private static string History => RhinoApp.CommandHistoryWindowText ?? string.Empty;

    public CommandInterceptor(RhinoDoc doc)
    {
        Doc = doc;
        RhinoApp.Idle += OnIdle;
    }

    public void Dispose() => RhinoApp.Idle -= OnIdle;

    private void OnIdle(object? sender, EventArgs e)
    {
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != Doc.RuntimeSerialNumber)
        {
            Active = false;
            return;
        }

        string history = History;
        if (!Active)   // just (re)activated: baseline so we only see commands typed from now on
        {
            Active = true;
            HistoryCursor = history.Length;
            return;
        }
        if (history.Length < HistoryCursor)   // history was cleared out from under us
        {
            HistoryCursor = history.Length;
            return;
        }
        if (history.Length == HistoryCursor)
            return;

        // Advance the cursor past the new text before routing, so our own echoed output lands
        // in the next scan instead of re-triggering this one.
        string fresh = history.Substring(HistoryCursor);
        HistoryCursor = history.Length;

        foreach (string line in fresh.Split('\n'))
        {
            string? request = ExtractRequest(line);
            if (request != null)
                Route(request);
        }
    }

    // The request text from a submitted `Command: "..."` line, or null if the line
    // isn't a sigil-led command submission (so plugin/agent output is never routed).
    private static string? ExtractRequest(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith(CommandPrefix, StringComparison.Ordinal))
            return null;
        string entry = trimmed.Substring(CommandPrefix.Length).TrimStart();
        if (entry.Length == 0 || entry[0] != Sigil)
            return null;
        string request = entry.Substring(1).Trim();
        return request.Length > 0 ? request : null;
    }

    private void Route(string request)
    {
        RhinoApp.WriteLine($"{RoutedMarker} {request}");

        // Control verbs act on the running turn immediately, bypassing the queue.
        switch (request.Trim().ToLowerInvariant())
        {
            case "cancel":
            case "stop":
                if (AgentHost.TryFindActive(Doc, out IAgentRunner running))
                    running.Cancel();
                else
                    RhinoApp.WriteLine($"{RoutedMarker} nothing running.");
                return;
            case "exit":
            case "quit":
                AgentHost.Stop(Doc);
                RhinoApp.WriteLine($"{RoutedMarker} agent stopped.");
                return;
        }

        AgentDispatch.PromptActive(Doc, UserMessage.FromText(request));
    }
}
