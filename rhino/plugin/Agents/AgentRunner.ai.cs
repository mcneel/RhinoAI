using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Rhino.UI;
using Rhino.AI.PlugIns;

namespace Rhino.AI;

internal sealed class AgentRunner : IDisposable
{
    private Agent Agent { get; }

    private object Gate { get; } = new();

    private CancellationTokenSource? TurnCancellation { get; set; }

    private bool HasAttached { get; set; }

    public IAgentDefinition Definition { get; }

    public Conversation Conversation { get; }

    public string Key => Definition.Id.ToString();

    public string Name => Definition.DisplayName;

    public bool Runs(Agent agent) => ReferenceEquals(Agent, agent);

    private AgentRunner(IAgentDefinition definition, Agent agent, Conversation conversation)
    {
        Definition = definition;
        Agent = agent;
        Conversation = conversation;
    }

    public static AgentRunner Start(IAgentDefinition definition, Agent agent, string docTitle) =>
        new(definition, agent, new Conversation(Guid.NewGuid(), definition.Id.ToString(), docTitle));

    public static AgentRunner Resume(IAgentDefinition definition, Agent agent, ConversationDto dto)
    {
        Conversation restored = Conversation.Restore(dto, definition.Id.ToString());
        AgentRunner runner = new(definition, agent, restored);

        if (definition is CliAgentDefinition)
            agent.Config.SessionId = restored.AgentSessionId;
        else
            restored.NoteSystem(LOC.STR("This agent cannot resume prior context; the turns above are shown for reference only."));

        return runner;
    }

    public async Task PromptAsync(UserMessage message, Uri mcpUrl, string cwd)
    {
        AttachMcps(mcpUrl);
        Agent.Harness.Config.CurrentWorkingDirectory = cwd;

        CancellationTokenSource cancellation = new();
        lock (Gate)
            TurnCancellation = cancellation;

        Conversation.BeginTurn(message.Text, [.. message.Attachments.Select(static a => a.Info)]);
        TurnRecorder recorder = new(Conversation);
        try
        {
            await foreach (ITurn turn in Agent.StreamAsync(Contents(message), cancellation.Token).ConfigureAwait(false))
                recorder.Record(turn);

            recorder.Finish();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Conversation.FailTurn(ex.Message);
        }
        finally
        {
            lock (Gate)
                TurnCancellation = null;
            cancellation.Dispose();

            if (Agent.Config.SessionId != Guid.Empty)
                Conversation.AdoptSessionId(Agent.Config.SessionId);

            Conversation.CompleteTurn();
            PersistTurn();
        }
    }

    public void Login() =>
        Conversation.NoteSystem(LOC.STR("Sign-in starts automatically with your next prompt."));

    public void Cancel()
    {
        lock (Gate)
            TurnCancellation?.Cancel();
    }

    public void Dispose() => Cancel();

    public bool HasAttachedMcps { get { lock (Gate) return HasAttached; } }

    // Before the first prompt this includes the plug-in MCPs that prompt will attach, so they can be listed and toggled early.
    public IReadOnlyList<Mcp> Mcps
    {
        get
        {
            lock (Gate)
            {
                List<Mcp> mcps = [.. Agent.Harness.Mcps.Values];
                if (HasAttached) return mcps;

                foreach (Mcp pending in PlugInRegistry.PanelMcps)
                    if (!mcps.Any(mcp => string.Equals(mcp.Name, pending.Name, StringComparison.OrdinalIgnoreCase)))
                        mcps.Add(pending);
                return mcps;
            }
        }
    }

    // Under the gate because the panel reads the harness dictionaries from the UI thread.
    private void AttachMcps(Uri mcpUrl)
    {
        lock (Gate)
            AttachMcpsLocked(mcpUrl);
    }

    private void AttachMcpsLocked(Uri mcpUrl)
    {
        if (HasAttached) return;

        Agent.Harness.AddMcp(new HttpMcp(RouterMcpConfig.ServerName, mcpUrl));

        foreach (Mcp mcp in PlugInRegistry.PanelMcps)
            Agent.Harness.AddMcp(mcp);

        foreach (ISkill skill in PlugInRegistry.PanelSkills)
            Agent.Harness.AddSkill(skill);

        HasAttached = true;
    }

    private static List<IMessageContent> Contents(UserMessage message)
    {
        List<IMessageContent> contents = new(message.Attachments.Count + 1);
        if (message.Text.Length > 0)
            contents.Add(new TextContent(message.Text));

        foreach (Attachment attachment in message.Attachments)
        {
            contents.Add(attachment.Kind switch
            {
                AttachmentKind.Image => new ImageContent(attachment.Data, attachment.MediaType),
                AttachmentKind.TextFile => new TextContent($"```{attachment.Name}\n{Encoding.UTF8.GetString(attachment.Data)}\n```"),
            });
        }

        return contents;
    }

    private void PersistTurn()
    {
        try
        {
            ConversationStore.Save(Conversation);
        }
        catch (Exception)
        {
            // A settings hiccup must never fault the turn the user just ran.
        }
    }
}
