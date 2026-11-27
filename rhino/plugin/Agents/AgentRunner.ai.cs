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

    private Dictionary<IMcpDefinition, Mcp> CustomMcpsByDefinition { get; } = [];

    private bool IsDisposed { get; set; }

    public Guid DefinitionId { get; }
    public IAgentDefinition? Definition => AIInstances.Agents.FromId(DefinitionId);

    public Conversation Conversation { get; }

    public string Key => DefinitionId.ToString();

    public string Name => Definition?.DisplayName ?? Key;

    public bool Runs(Agent agent) => ReferenceEquals(Agent, agent);

    private AgentRunner(IAgentDefinition definition, Agent agent, Conversation conversation)
    {
        DefinitionId = definition.Id;
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
        CancellationTokenSource cancellation;
        lock (Gate)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(AgentRunner));

            cancellation = new();
            TurnCancellation = cancellation;
        }

        AttachMcps(mcpUrl);
        Agent.Harness.Config.CurrentWorkingDirectory = cwd;

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
            bool isDisposed;
            lock (Gate)
            {
                TurnCancellation = null;
                isDisposed = IsDisposed;
            }
            cancellation.Dispose();

            if (Agent.Config.SessionId != Guid.Empty)
                Conversation.AdoptSessionId(Agent.Config.SessionId);

            Conversation.CompleteTurn();
            PersistTurn();

            if (isDisposed)
                DisposeHarness();
        }
    }

    public void Login() =>
        Conversation.NoteSystem(LOC.STR("Sign-in starts automatically with your next prompt."));

    public void Cancel()
    {
        lock (Gate)
            TurnCancellation?.Cancel();
    }

    // A turn in flight may be mid tool call on the harness, so it disposes the harness itself once it unwinds.
    public void Dispose()
    {
        bool isTurnRunning;
        lock (Gate)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            isTurnRunning = TurnCancellation is not null;
            TurnCancellation?.Cancel();
        }

        if (!isTurnRunning)
            DisposeHarness();
    }

    private void DisposeHarness()
    {
        if (Agent.Harness is IDisposable harness)
            harness.Dispose();

        Mcp[] customMcps;
        lock (Gate)
            customMcps = [.. CustomMcpsByDefinition.Values];
        foreach (Mcp mcp in customMcps)
            mcp.Dispose();
    }

    public bool HasAttachedMcps { get { lock (Gate) return HasAttached; } }

    // Before the first prompt this includes the plug-in and custom MCPs that prompt will attach, so they can be listed and toggled early.
    public IReadOnlyList<Mcp> Mcps
    {
        get
        {
            lock (Gate)
            {
                List<Mcp> mcps = [.. Agent.Harness.Mcps.Values];
                if (HasAttached) return mcps;

                foreach (Mcp pending in PlugInRegistry.PanelMcps.Concat(CustomMcpsLocked()))
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

        foreach (Mcp mcp in CustomMcpsLocked())
            Agent.Harness.AddMcp(mcp);

        foreach (ISkill skill in PlugInRegistry.PanelSkills)
            Agent.Harness.AddSkill(skill);

        HasAttached = true;
    }

    // Read from preferences each time so a server added in Settings before the first prompt still attaches, while reused instances keep earlier panel toggles.
    private List<Mcp> CustomMcpsLocked()
    {
        List<Mcp> mcps = [];
        foreach (IMcpDefinition definition in AIInstances.Preferences.CustomMcps)
        {
            if (!Tool.IsCanonicalName(definition.Name)) continue;

            if (!CustomMcpsByDefinition.TryGetValue(definition, out Mcp? mcp))
            {
                mcp = ToMcp(definition);
                CustomMcpsByDefinition[definition] = mcp;
            }
            mcps.Add(mcp);
        }
        return mcps;
    }

    private static Mcp ToMcp(IMcpDefinition definition) => definition switch
    {
        StdioMcpDefinition stdio => new StdioMcp(stdio.Name, stdio.Command, stdio.Args, stdio.Env.ToDictionary(pair => pair.Key, pair => pair.Value)) { Enabled = stdio.Enabled },
        HttpMcpDefinition http => new HttpMcp(http.Name, http.Url, http.Headers.ToDictionary(pair => pair.Key, pair => pair.Value)) { Enabled = http.Enabled },
        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition, "Unknown MCP definition."),
    };

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
