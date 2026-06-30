using System.Text.Json;
using Acp;

namespace Acp.Tests;

[TestFixture]
public sealed class RobustnessTests
{
    [Test]
    [CancelAfter(10000)]
    public async Task Malformed_lines_are_skipped_and_the_read_loop_survives()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        // `a` is raw here (no client connection) so we can inject hostile input directly.
        await a.WriteLineAsync("this is not json");
        await a.WriteLineAsync("");
        await a.WriteLineAsync("[1,2,3]"); // valid JSON, but not an object

        JsonRpcRequest request = new()
        {
            Id = RequestId.Of(1),
            Method = AgentMethods.Initialize,
            Params = JsonSerializer.SerializeToElement(new InitializeRequest { ProtocolVersion = 1 }, AcpJson.Options),
        };
        await a.WriteLineAsync(JsonSerializer.Serialize(request, AcpJson.Options));

        string? line = await a.ReadLineAsync();
        Assert.That(line, Is.Not.Null);
        JsonRpcResponse response = JsonSerializer.Deserialize<JsonRpcResponse>(line!, AcpJson.Options)!;
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Id, Is.EqualTo(RequestId.Of(1)));
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Brace_wrapped_garbage_passes_the_guard_but_does_not_kill_the_loop()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        // First and last char are braces (the read loop's only cheap guard), but the body is not
        // valid JSON: JsonDocument.Parse throws. Previously this escaped DispatchAsync, killed the
        // read loop, and FailPending'd every in-flight request.
        await a.WriteLineAsync("{not valid json}");
        await a.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":}"); // brace-wrapped, malformed

        JsonRpcRequest request = new()
        {
            Id = RequestId.Of(2),
            Method = AgentMethods.Initialize,
            Params = JsonSerializer.SerializeToElement(new InitializeRequest { ProtocolVersion = 1 }, AcpJson.Options),
        };
        await a.WriteLineAsync(JsonSerializer.Serialize(request, AcpJson.Options));

        string? line = await a.ReadLineAsync();
        Assert.That(line, Is.Not.Null);
        JsonRpcResponse response = JsonSerializer.Deserialize<JsonRpcResponse>(line!, AcpJson.Options)!;
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Id, Is.EqualTo(RequestId.Of(2)));
    }

    [Test]
    [CancelAfter(10000)]
    public async Task A_frame_with_a_valid_id_but_an_undeserializable_request_gets_an_InvalidRequest_reply()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        // Parses as JSON and has both id + method, so it reaches Deserialize<JsonRpcRequest>, which
        // throws (method is a number, not the required string). The id is well-formed, so TryReadId
        // succeeds and the loop replies with InvalidRequest instead of silently skipping the frame.
        await a.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":5}");

        string? line = await a.ReadLineAsync();
        Assert.That(line, Is.Not.Null);
        JsonRpcResponse response = JsonSerializer.Deserialize<JsonRpcResponse>(line!, AcpJson.Options)!;
        Assert.That(response.Id, Is.EqualTo(RequestId.Of(1)));
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error!.Code, Is.EqualTo((int)JsonRpcErrorCode.InvalidRequest));
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Clean_stream_close_completes_the_read_loop_without_faulting()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        a.Dispose(); // close the peer end -> ReadLineAsync returns null -> loop ends cleanly

        await agentConn.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(agentConn.Completion.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void Completion_throws_before_Start_and_a_second_Start_is_a_no_op()
    {
        (IAcpTransport _, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;

        Assert.Throws<InvalidOperationException>(() => _ = agentConn.Completion);

        agentConn.Start();
        Task first = agentConn.Completion;
        agentConn.Start();
        Assert.That(agentConn.Completion, Is.SameAs(first));
    }
}
