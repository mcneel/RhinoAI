using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Acp;
using RhMcp;

namespace RhMcp.StreamJson.Tests;

// End-to-end turn orchestration of the real AgentRunner over a classicist FakeAcpAgent (a genuine
// second IAcpAgent implementation, not a mock) wired to the runner's own RhinoAcpClient. This closes
// the only end-to-end gap in the single-seam path: the loopback test opens the turn by hand, so the
// contract that AgentRunner itself opens exactly one turn, notes the session-started lifecycle once,
// records updates INSIDE that open turn, and persists on completion, was asserted nowhere. Here the
// runner drives the whole begin -> initialize/new-session -> prompt -> complete -> save sequence.
[TestFixture]
public sealed class AgentRunnerTurnTests
{
    [SetUp]
    public void Reset() => AISettings.ResetForTest();

    [TearDown]
    public void Cleanup() => AISettings.ResetForTest();

    private static AgentDefinition ClaudeDef() =>
        new("claude", AgentAdapter.Claude, "claude", [], string.Empty, [], string.Empty, Enabled: true, IsBuiltin: true);

    [Test]
    [CancelAfter(10000)]
    public async Task PromptAsync_opens_one_turn_records_inside_it_notes_the_session_once_and_saves()
    {
        FakeAcpAgent fake = new();
        AgentRunner runner = new(ClaudeDef(), "Untitled", (client, _, _) => fake.AttachTo(client));

        await runner.PromptAsync(UserMessage.FromText("draw a box"), "http://localhost:10500/agent", "/tmp");

        Conversation convo = runner.Conversation;

        // Exactly one turn opened (BeginTurn), and it is closed (CompleteTurn ran in the finally).
        Assert.That(convo.Turns, Has.Count.EqualTo(1));
        Turn turn = convo.Turns[0];
        Assert.That(turn.Prompt, Is.EqualTo("draw a box"));
        Assert.That(turn.Completed, Is.True);

        // The session-started lifecycle was noted exactly once (the first prompt starts the session).
        Assert.That(convo.Lifecycle, Has.Count.EqualTo(1));
        Assert.That(convo.Lifecycle[0].Kind, Is.EqualTo(TurnEventKind.SessionStarted));

        // The agent's update streamed through the runner's RhinoAcpClient and landed INSIDE the turn,
        // not dropped for want of a current turn (which is what would happen if BeginTurn hadn't run).
        Assert.That(turn.Events, Has.Count.EqualTo(1));
        Assert.That(turn.Events[0].Kind, Is.EqualTo(TurnEventKind.AssistantText));
        Assert.That(turn.Events[0].Text, Is.EqualTo("on it"));

        // The runner initialized then opened a session against the doc's MCP url (once).
        Assert.That(fake.InitializeCount, Is.EqualTo(1));
        Assert.That(fake.NewSessionCount, Is.EqualTo(1));
        Assert.That(fake.PromptCount, Is.EqualTo(1));

        // CompleteTurn triggered the persistence: the transcript is in the store keyed by session id.
        Assert.That(ConversationStore.TryLoad(convo.AgentSessionId.ToString(), out ConversationDto saved), Is.True);
        Assert.That(saved.Turns, Has.Count.EqualTo(1));
        Assert.That(saved.Turns[0].Prompt, Is.EqualTo("draw a box"));
        Assert.That(saved.Lifecycle, Has.Count.EqualTo(1));
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Second_PromptAsync_reuses_the_session_opening_a_second_turn_without_re_initializing()
    {
        FakeAcpAgent fake = new();
        AgentRunner runner = new(ClaudeDef(), "Untitled", (client, _, _) => fake.AttachTo(client));

        await runner.PromptAsync(UserMessage.FromText("first"), "http://localhost:10500/agent", "/tmp");
        await runner.PromptAsync(UserMessage.FromText("second"), "http://localhost:10500/agent", "/tmp");

        Conversation convo = runner.Conversation;

        Assert.That(convo.Turns, Has.Count.EqualTo(2));
        Assert.That(convo.Turns.Select(static t => t.Prompt), Is.EqualTo(new[] { "first", "second" }));

        // Initialize + new-session happen once for the life of the runner; only PromptAsync repeats.
        Assert.That(fake.InitializeCount, Is.EqualTo(1));
        Assert.That(fake.NewSessionCount, Is.EqualTo(1));
        Assert.That(fake.PromptCount, Is.EqualTo(2));

        // Still a single session-started lifecycle event across both turns.
        Assert.That(convo.Lifecycle, Has.Count.EqualTo(1));
    }

    // A real (classicist) IAcpAgent the runner drives: it captures the runner's own RhinoAcpClient and,
    // on each prompt, streams one assistant-message update back through it exactly as a live agent's
    // read loop would, so recording flows through the real seam. Counters pin the lifecycle calls.
    private sealed class FakeAcpAgent : IAcpAgent
    {
        private IAcpClient? Client { get; set; }
        private string SessionId { get; set; } = string.Empty;

        public int InitializeCount { get; private set; }
        public int NewSessionCount { get; private set; }
        public int PromptCount { get; private set; }

        public IAcpAgent AttachTo(IAcpClient client)
        {
            Client = client;
            return this;
        }

        public ValueTask<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken cancellationToken = default)
        {
            InitializeCount++;
            return new ValueTask<InitializeResponse>(new InitializeResponse { ProtocolVersion = ProtocolConstants.Version });
        }

        public ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest request, CancellationToken cancellationToken = default)
        {
            NewSessionCount++;
            SessionId = "session-" + NewSessionCount;
            return new ValueTask<NewSessionResponse>(new NewSessionResponse { SessionId = SessionId });
        }

        public async ValueTask<PromptResponse> SessionPromptAsync(PromptRequest request, CancellationToken cancellationToken = default)
        {
            PromptCount++;
            IAcpClient client = Client ?? throw new InvalidOperationException("FakeAcpAgent was not attached to a client.");
            await client.SessionUpdateAsync(new SessionNotification
            {
                SessionId = SessionId,
                Update = new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = "on it" } },
            }, cancellationToken).ConfigureAwait(false);
            return new PromptResponse { StopReason = StopReason.EndTurn };
        }

        public ValueTask SessionCancelAsync(CancelNotification notification, CancellationToken cancellationToken = default) => default;

        public ValueTask<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken = default) =>
            new(new AuthenticateResponse());

        public ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest request, CancellationToken cancellationToken = default) =>
            new(new LoadSessionResponse());

        public ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest request, CancellationToken cancellationToken = default) =>
            new(new SetSessionModeResponse());

        public ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) =>
            new(default(JsonElement));

        public ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default) => default;
    }
}
