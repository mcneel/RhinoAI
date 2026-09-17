using System.Threading;
using System.Threading.Tasks;

using Eto.Forms;

namespace Rhino.AI.Server;

// The one reader of per-tool modes. Only the in-Rhino profile routes consult it: an external MCP
// client has its own permission UI and is never gated here.
internal static class ToolPolicy
{
    // Profile-specific starting modes on top of what a tool declares, so each panel arrives focused
    // on its own job. The user overrides any of it per panel.
    //
    // Nothing starts switched off. A tool the agent cannot call is a tool the user never gets asked
    // about, so it looks the same as one that is simply never useful — and the panel quietly loses
    // an ability the user might have wanted. Off stays available as a choice; it is just not a
    // starting position.
    public static ToolMode DefaultMode(AIProfile profile, string name, ToolMode declared)
    {
        // Asking what it is allowed to do is not itself something to ask about, and the answer can
        // save the user a refusal.
        if (name is "list_enabled")
            return ToolMode.On;

        // Another assistant's tools, and the shared Rhino toolbox every panel draws on, ask first:
        // useful here, but not what this panel is for, so their calls are worth a glance.
        if (Owner(name) != profile)
            return ToolMode.Ask;

        // Taking a picture and interrogating their geometry are the two the Rhino panel's agent
        // reaches for often enough that the user wants a say, whatever they declare.
        if (profile == AIProfile.Rhino && name is "get_viewport_image" or "get_object_properties")
            return ToolMode.Ask;

        // Its own tools run as declared, except that a tool declaring itself off starts by asking.
        return declared == ToolMode.Off ? ToolMode.Ask : declared;
    }

    public static ToolMode DefaultMode(AIProfile profile, ToolInfo tool) => DefaultMode(profile, tool.Name, tool.DefaultMode);

    // What the Rhino assistant is for, as opposed to a resource every assistant draws on: running
    // something in Rhino — a command, a script — and asking the model what it is or taking its
    // picture. The other two have a canvas and an editor to work in; this is what working in Rhino
    // itself consists of.
    private static IReadOnlySet<string> RhinoTools { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "run_command",
        "run_python",
        "run_csharp",
        "get_object_properties",
        "get_viewport_image",
    };

    // The assistant a tool exists for, or null when it is part of the shared Rhino toolbox every panel
    // draws on — reading the document, moving the camera, opening and saving. Read by the settings
    // page, which puts an assistant's own tools in a table of their own, and by the panel's own
    // permission menu, which offers them first.
    public static AIProfile? Owner(string name) =>
        name.StartsWith("script_editor_", StringComparison.OrdinalIgnoreCase) ? AIProfile.Script
        : name.StartsWith("g1_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("g2_", StringComparison.OrdinalIgnoreCase) ? AIProfile.Grasshopper
        : RhinoTools.Contains(name) ? AIProfile.Rhino
        : null;

    public static ToolMode Mode(AIProfile profile, string name, ToolMode declared) =>
        AISettings.ToolModeFor(profile, name, DefaultMode(profile, name, declared));

    public static ToolMode Mode(AIProfile profile, ToolInfo tool) => Mode(profile, tool.Name, tool.DefaultMode);

    public static ToolMode Mode(AIProfile profile, ToolHandler tool) => Mode(profile, tool.Name, tool.DefaultMode);

    public static void SetMode(AIProfile profile, ToolInfo tool, ToolMode mode) =>
        AISettings.SetToolMode(profile, tool.Name, mode, DefaultMode(profile, tool));

    public static bool IsAvailable(AIProfile profile, ToolHandler tool) => Mode(profile, tool) != ToolMode.Off;

    public static bool NeedsConfirmation(AIProfile profile, ToolHandler tool) => Mode(profile, tool) == ToolMode.Ask;

    // Asks in the chat, where the user is already looking: the request goes into the conversation as a
    // card and the tool call waits on its answer. A cancelled turn answers no, so nothing is left
    // hanging. The modal below is only for a call with no conversation to ask in.
    public static async Task<bool> ConfirmAsync(
        AIProfile profile, RhinoDoc? doc, ToolHandler tool, IDictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        // One hop to the UI thread for both halves: AgentHost's dictionaries are UI-thread-owned, and a
        // tool describing itself may read the document.
        AskContext ask = await PrepareAsync(doc, profile, tool, arguments).ConfigureAwait(false);

        if (ask.Conversation is null)
            return await ConfirmModalAsync(profile, tool, ask.Detail).ConfigureAwait(false);

        PermissionRequest request = new(
            tool.Name,
            string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title!,
            ask.Detail);

        Conversation conversation = ask.Conversation;
        conversation.AddPendingPermission(request);
        try
        {
            using CancellationTokenRegistration cancelled = ct.Register(() => request.Resolve(false));
            return await request.Decision.ConfigureAwait(false);
        }
        finally
        {
            // Withdraws the card however this ended, so an answered request never stays on screen.
            conversation.RemovePendingPermission(request);
        }
    }

    // Where to ask, and what to show when asking.
    private readonly record struct AskContext(Conversation? Conversation, string Detail);

    private static Task<AskContext> PrepareAsync(
        RhinoDoc? doc, AIProfile profile, ToolHandler tool, IDictionary<string, JsonElement>? arguments)
    {
        TaskCompletionSource<AskContext> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try
            {
                Conversation? conversation = doc is not null && AgentHost.TryFor(doc, profile, out IAgentRunner agent)
                    ? agent.Conversation
                    : null;
                tcs.SetResult(new AskContext(conversation, Detail(tool, arguments)));
            }
            catch (Exception ex) { tcs.SetException(ex); }
        }));
        return tcs.Task;
    }

    // The fallback: a native Yes/No, for a call that reached us with no panel conversation behind it.
    private static Task<bool> ConfirmModalAsync(AIProfile profile, ToolHandler tool, string detail)
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try
            {
                DialogResult answer = MessageBox.Show(
                    Rhino.UI.RhinoEtoApp.MainWindow, Prompt(tool, detail), AIProfiles.Name(profile),
                    MessageBoxButtons.YesNo, MessageBoxType.Question);
                tcs.SetResult(answer == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }));
        return tcs.Task;
    }

    private const int MaxArgumentChars = 600;

    // A described call can be a whole script, which the card scrolls; past this it is not being read.
    private const int MaxDetailChars = 4000;

    private static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true };

    // The tool's own account of what it is about to do where it has one, its arguments otherwise.
    // For a run that is the script itself, which is the thing the user actually wants to see.
    private static string Detail(ToolHandler tool, IDictionary<string, JsonElement>? arguments) =>
        tool.Describe(arguments) is { Length: > 0 } described
            ? Truncate(described, MaxDetailChars)
            : Summarize(arguments);

    private static string Prompt(ToolHandler tool, string detail)
    {
        string title = string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title!;
        return detail.Length == 0
            ? $"The AI agent wants to use \"{title}\".\n\nAllow it?"
            : $"The AI agent wants to use \"{title}\" with:\n\n{detail}\n\nAllow it?";
    }

    private static string Summarize(IDictionary<string, JsonElement>? arguments) =>
        arguments is null || arguments.Count == 0
            ? string.Empty
            : Truncate(JsonSerializer.Serialize(arguments, Indented), MaxArgumentChars);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
