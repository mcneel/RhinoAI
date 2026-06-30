using System;
using System.Threading;
using System.Threading.Tasks;
using Acp;

namespace RhMcp;

// Drives any ACP agent (a native connection like Gemini, or a StreamJsonAgent wrapping a stream-json
// CLI like Claude/Codex) behind the plugin's IAgentRunner seam. One ACP session per agent instance:
// the first prompt initializes the connection and opens a session pointed at this doc's rhino MCP
// server; later prompts reuse it. Streaming arrives out-of-band through RhinoAcpClient on the
// connection's read loop; the prompt response ends the turn.
internal sealed class AgentRunner : IAgentRunner
{
    private AgentDefinition Definition { get; }
    private RhinoAcpClient Client { get; }
    private Func<IAcpClient, Conversation, string, IAcpAgent> Connect { get; }
    private SemaphoreSlim TurnGate { get; } = new(1, 1);
    private object Gate { get; } = new();

    private IAcpAgent? Connection { get; set; }

    // A real token from SessionNewAsync once Started is true, never read before then (PromptAsync
    // goes through EnsureStartedAsync first); the initial empty string is just the pre-start value,
    // not an absence sentinel.
    private string SessionId { get; set; } = string.Empty;

    private bool Started { get; set; }

    public AgentRunner(AgentDefinition def, string docTitle, Func<IAcpClient, Conversation, string, IAcpAgent> connect)
        : this(def, new Conversation(Guid.NewGuid(), def.Name, docTitle), connect)
    {
    }

    // Resume path: drive a restored past Conversation (its prior turns are already shown) rather than
    // a fresh one. The connection factory is expected to seed the CLI's --resume id from the restored
    // conversation's AgentSessionId, so the next prompt continues the agent's prior context.
    public AgentRunner(AgentDefinition def, Conversation conversation, Func<IAcpClient, Conversation, string, IAcpAgent> connect)
    {
        Definition = def;
        Connect = connect;
        Conversation = conversation;
        Client = new RhinoAcpClient(Conversation);
    }

    public string Name => Definition.Name;

    public Conversation Conversation { get; }

    public async Task PromptAsync(UserMessage message, string mcpUrl, string cwd)
    {
        await TurnGate.WaitAsync().ConfigureAwait(false);
        try
        {
            IAcpAgent connection = await EnsureStartedAsync(mcpUrl, cwd).ConfigureAwait(false);
            Conversation.BeginTurn(message.Text);
            await connection.SessionPromptAsync(new PromptRequest
            {
                SessionId = SessionId,
                Prompt = AcpMessageMapper.Prompt(message),
            }).ConfigureAwait(false);
        }
        finally
        {
            Conversation.CompleteTurn();
            PersistTurn();
            TurnGate.Release();
        }
    }

    public void Cancel()
    {
        IAcpAgent? connection;
        string sessionId;
        lock (Gate)
        {
            connection = Connection;
            sessionId = SessionId;
        }
        if (connection is not null)
            _ = connection.SessionCancelAsync(new CancelNotification { SessionId = sessionId });
    }

    public void Dispose()
    {
        IAcpAgent? connection;
        lock (Gate)
            connection = Connection;
        (connection as IDisposable)?.Dispose();
        // TurnGate is deliberately not disposed: a turn cancelled by this teardown still runs its
        // finally (TurnGate.Release), and disposing here would race it into ObjectDisposedException.
    }

    private async Task<IAcpAgent> EnsureStartedAsync(string mcpUrl, string cwd)
    {
        if (Started)
        {
            lock (Gate)
                return Connection ?? throw new InvalidOperationException("Connection missing after start.");
        }

        IAcpAgent connection = Connect(Client, Conversation, cwd);
        try
        {
            await connection.InitializeAsync(new InitializeRequest { ProtocolVersion = ProtocolConstants.Version }).ConfigureAwait(false);
            NewSessionResponse session = await connection.SessionNewAsync(new NewSessionRequest
            {
                Cwd = cwd,
                McpServers = [new HttpMcpServer { Name = "rhino", Url = mcpUrl, Headers = [] }],
            }).ConfigureAwait(false);

            lock (Gate)
            {
                Connection = connection;
                SessionId = session.SessionId;
                Started = true;
            }
            Conversation.NoteSessionStarted();
            return connection;
        }
        catch
        {
            (connection as IDisposable)?.Dispose();
            throw;
        }
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
