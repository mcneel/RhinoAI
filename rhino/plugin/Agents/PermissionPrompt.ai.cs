using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI;

internal static class PermissionPrompt
{
    public static async Task AskAsync(IPermissionRequest request, CancellationToken token)
    {
        if (request is not PermissionRequest permission)
            return;

        PendingPermission pending = new(permission.McpName, permission.ToolName, permission.DeclaredArgs, DescribeArgs(permission.Args));
        using CancellationTokenRegistration registration = token.Register(() => pending.Decide(false));

        Conversation? shownIn = await OnUiThreadAsync(() => Show(permission.Agent, pending)).ConfigureAwait(false);
        try
        {
            permission.HasPermission = await pending.Decision.ConfigureAwait(false);
        }
        finally
        {
            shownIn?.RemovePendingPermission(pending);
        }
    }

    public static void AllowAlways(PendingPermission pending)
    {
        Dictionary<string, ToolArgPermission> anyArgs = new(pending.DeclaredArgs.Count);
        foreach (string arg in pending.DeclaredArgs)
            anyArgs[arg] = new ToolArgPermission(arg, ArgRule.Anything);

        AIInstances.Preferences.Permissions.Replace(new Permission(pending.McpName, pending.ToolName, Permissability.Always, anyArgs));
        pending.Decide(true);
    }

    private static Conversation? Show(Agent agent, PendingPermission pending)
    {
        if (pending.IsDecided)
            return null;

        if (AgentHost.PanelConversationFor(agent) is { } conversation)
        {
            conversation.AddPendingPermission(pending);
            return conversation;
        }

        pending.Decide(AskWithDialog(pending));
        return null;
    }

    private static bool AskWithDialog(PendingPermission pending)
    {
        DialogResult result = MessageBox.Show(
            RhinoEtoApp.MainWindow,
            string.Format(LOC.STR("Allow the agent to use \"{0}\" from \"{1}\"?"), pending.ToolName, pending.McpName),
            LOC.STR("Tool Permission"),
            MessageBoxButtons.YesNo,
            MessageBoxType.Question);
        return result == DialogResult.Yes;
    }

    private static string DescribeArgs(IReadOnlyList<IToolArg> args)
    {
        JsonObject described = new();
        foreach (IToolArg arg in args)
            described[arg.Name] = arg switch
            {
                ToolSecret => JsonValue.Create("******"),
                ToolString text => JsonValue.Create(text.Value),
                ToolPath path => JsonValue.Create(path.Value),
                ToolUrl url => JsonValue.Create(url.Value),
                ToolBoolean flag => JsonValue.Create(flag.Value),
                ToolNumber number => JsonValue.Create(number.Value),
                ToolInt integer => JsonValue.Create(integer.Value),
                ToolJson json => JsonNode.Parse(json.Value.GetRawText()),
                _ => null,
            };
        return described.ToJsonString();
    }

    private static Task<T> OnUiThreadAsync<T>(Func<T> work)
    {
        TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        }));
        return result.Task;
    }
}
