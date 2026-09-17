using System.Reflection;

using Eto.Forms;

namespace Rhino.AI.Server;

// Says when a running tool has stopped and is waiting for the user at Rhino's command line: a script
// that calls rs.GetObject(), a run_command that starts _Line. Without it the card spins with no hint
// that Rhino is asking a question somewhere else on screen, which reads as a hang.
//
// Nothing is inferred. Rhino tracks the active getter itself (CRhinoDoc::InGet, the same flag the
// command line runs on) and raises CommandPromptChanged from inside the getter's own message loop,
// so the panel can be told the moment one opens, in the getter's own words. There is no matching
// "getter closed" notification, hence the slow tick: it is only there to notice a get that ended
// without anything resetting the prompt.
//
// Everything here runs on the UI thread. Watches nest because tool calls can overlap.
internal static class UserPromptWatch
{
    private const double TickSeconds = 0.25;

    private static int Depth { get; set; }
    private static RhinoDoc? Watched { get; set; }
    private static UITimer? Ticker { get; set; }

    // Held for as long as a tool call is in flight. A tool with no document cannot reach a getter.
    public static IDisposable While(RhinoDoc? doc)
    {
        if (doc is null)
            return Unwatched;

        RhinoApp.InvokeOnUiThread(new Action(() => Enter(doc)));
        return new Scope();
    }

    private static IDisposable Unwatched { get; } = new NoScope();

    private sealed class NoScope : IDisposable
    {
        public void Dispose() { }
    }

    // The call that opened the watch may resume on another thread, so leaving marshals back.
    private sealed class Scope : IDisposable
    {
        private bool Left { get; set; }

        public void Dispose()
        {
            if (Left)
                return;
            Left = true;
            RhinoApp.InvokeOnUiThread(new Action(Leave));
        }
    }

    private static void Enter(RhinoDoc doc)
    {
        Watched = doc;
        if (++Depth > 1)
            return;

        if (Ticker is null)
        {
            Ticker = new UITimer { Interval = TickSeconds };
            Ticker.Elapsed += (_, _) => Refresh();
        }

        RhinoApp.CommandPromptChanged += OnCommandPromptChanged;
        Ticker.Start();
    }

    private static void Leave()
    {
        if (Depth == 0 || --Depth > 0)
            return;

        RhinoApp.CommandPromptChanged -= OnCommandPromptChanged;
        Ticker?.Stop();
        Publish(null);
        Watched = null;
    }

    private static void OnCommandPromptChanged(object? sender, Rhino.UI.CommandPromptChangedEventArgs e) => Refresh();

    private static void Refresh() => Publish(Watched is { } doc ? Describe(doc) : null);

    private static void Publish(string? phrase)
    {
        if (ToolActivity.Waiting == phrase)
            return;

        ToolActivity.Waiting = phrase;

        // The phrase is not in the transcript, so nothing else would make a panel look again. Every
        // agent already on the document, because the call may be the other panel's — and only those,
        // since an external MCP client's call must not spawn a CLI just to say Rhino is busy.
        if (Watched is not { } doc)
            return;
        foreach (IAgentRunner agent in AgentHost.Live(doc))
            agent.Conversation.Touch();
    }

    // The command line already says what it wants in the words the user will read there, so that is
    // what the card says too; the fallback is for a get that prompts with nothing.
    private static string? Describe(RhinoDoc doc)
    {
        if (!InGet(doc))
            return null;

        string prompt = (RhinoApp.CommandPrompt ?? string.Empty).Trim().TrimEnd('.', ':');
        return prompt.Length == 0 ? "waiting for you — look at the command line" : $"waiting for you — {prompt}";
    }

    // RhinoDoc.InGetPoint is public; the general flag and the object getter are internal to
    // RhinoCommon and this plug-in compiles against the package, so they are read off the assembly
    // Rhino loaded. Losing them costs only the getters that are not point getters.
    private static PropertyInfo? AnyGet { get; } = Flag("InGet");

    private static PropertyInfo? ObjectGet { get; } = Flag("InGetObject");

    private static PropertyInfo? Flag(string name) =>
        typeof(RhinoDoc).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool InGet(RhinoDoc doc) => doc.InGetPoint || Reads(AnyGet, doc) || Reads(ObjectGet, doc);

    private static bool Reads(PropertyInfo? flag, RhinoDoc doc)
    {
        try
        {
            return flag?.GetValue(doc) is true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MethodAccessException)
        {
            return false;
        }
    }
}
