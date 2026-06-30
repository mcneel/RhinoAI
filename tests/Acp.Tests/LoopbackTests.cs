using Acp;

namespace Acp.Tests;

[TestFixture]
public sealed class LoopbackTests
{
    [Test]
    [CancelAfter(10000)]
    public async Task Initialize_new_prompt_streams_update_then_completes()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();

        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        CapturingClient client = new();
        ClientSideConnection clientConn = new(_ => client, a);
        clientConn.Start();

        InitializeResponse init = await clientConn.InitializeAsync(new InitializeRequest { ProtocolVersion = ProtocolConstants.Version });
        Assert.That(init.ProtocolVersion, Is.EqualTo(ProtocolConstants.Version));

        NewSessionResponse session = await clientConn.SessionNewAsync(new NewSessionRequest { Cwd = "/tmp", McpServers = [] });
        Assert.That(session.SessionId, Is.EqualTo("s1"));

        PromptResponse prompt = await clientConn.SessionPromptAsync(new PromptRequest
        {
            SessionId = session.SessionId,
            Prompt = [new TextContentBlock { Text = "hello" }],
        });

        Assert.That(prompt.StopReason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(client.Updates, Has.Count.EqualTo(1));
        Assert.That(client.Updates[0].Update, Is.TypeOf<AgentMessageChunkSessionUpdate>());
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Cancel_notification_reaches_the_agent()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();
        ClientSideConnection clientConn = new(_ => new CapturingClient(), a);
        clientConn.Start();

        await clientConn.SessionCancelAsync(new CancelNotification { SessionId = "s1" });

        // Notifications are fire-and-forget; give the peer's read loop a moment to deliver it.
        for (int i = 0; i < 50 && !agent.Cancelled; i++)
            await Task.Delay(10);
        Assert.That(agent.Cancelled, Is.True);
    }
}
