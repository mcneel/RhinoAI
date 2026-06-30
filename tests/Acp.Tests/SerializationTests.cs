using System.Text.Json;
using Acp;

namespace Acp.Tests;

[TestFixture]
public sealed class SerializationTests
{
    private static T Roundtrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AcpJson.Options), AcpJson.Options)!;

    [Test]
    public void ContentBlock_text_roundtrips_to_its_variant()
    {
        ContentBlock block = new TextContentBlock { Text = "hello" };
        ContentBlock back = Roundtrip(block);

        Assert.That(back, Is.TypeOf<TextContentBlock>());
        Assert.That(((TextContentBlock)back).Text, Is.EqualTo("hello"));
    }

    [Test]
    public void ContentBlock_image_keeps_data_and_mime()
    {
        ContentBlock block = new ImageContentBlock { Data = "AAAA", MimeType = "image/png" };
        string json = JsonSerializer.Serialize(block, AcpJson.Options);

        Assert.That(json, Does.Contain("\"type\":\"image\""));
        ImageContentBlock back = (ImageContentBlock)JsonSerializer.Deserialize<ContentBlock>(json, AcpJson.Options)!;
        Assert.That(back.MimeType, Is.EqualTo("image/png"));
    }

    [Test]
    public void SessionUpdate_agent_message_chunk_roundtrips()
    {
        SessionUpdate update = new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = "hi" } };
        SessionUpdate back = Roundtrip(update);

        Assert.That(back, Is.TypeOf<AgentMessageChunkSessionUpdate>());
        Assert.That(((AgentMessageChunkSessionUpdate)back).Content, Is.TypeOf<TextContentBlock>());
    }

    [Test]
    public void McpServer_http_roundtrips_with_type_tag()
    {
        McpServer server = new HttpMcpServer { Name = "rhino", Url = "http://localhost:1/agent", Headers = [] };
        string json = JsonSerializer.Serialize(server, AcpJson.Options);

        Assert.That(json, Does.Contain("\"type\":\"http\""));
        Assert.That(JsonSerializer.Deserialize<McpServer>(json, AcpJson.Options), Is.TypeOf<HttpMcpServer>());
    }

    [Test]
    public void McpServer_without_type_defaults_to_stdio()
    {
        const string json = """{"name":"x","command":"echo","args":[],"env":[]}""";
        McpServer? server = JsonSerializer.Deserialize<McpServer>(json, AcpJson.Options);

        Assert.That(server, Is.TypeOf<StdioMcpServer>());
        Assert.That(((StdioMcpServer)server!).Command, Is.EqualTo("echo"));
    }

    [Test]
    public void StopReason_serializes_snake_case()
    {
        Assert.That(JsonSerializer.Serialize(StopReason.EndTurn, AcpJson.Options), Is.EqualTo("\"end_turn\""));
        Assert.That(JsonSerializer.Deserialize<StopReason>("\"max_turn_requests\"", AcpJson.Options),
            Is.EqualTo(StopReason.MaxTurnRequests));
    }

    [Test]
    public void PromptRequest_roundtrips_mixed_content()
    {
        PromptRequest request = new()
        {
            SessionId = "s1",
            Prompt = [new TextContentBlock { Text = "make a box" }, new ImageContentBlock { Data = "AAAA", MimeType = "image/png" }],
        };
        PromptRequest back = Roundtrip(request);

        Assert.That(back.SessionId, Is.EqualTo("s1"));
        Assert.That(back.Prompt, Has.Length.EqualTo(2));
        Assert.That(back.Prompt[0], Is.TypeOf<TextContentBlock>());
        Assert.That(back.Prompt[1], Is.TypeOf<ImageContentBlock>());
    }

    [Test]
    public void Optional_nulls_are_omitted_on_write()
    {
        string json = JsonSerializer.Serialize(new TextContentBlock { Text = "x" }, AcpJson.Options);
        Assert.That(json, Does.Not.Contain("_meta"));
        Assert.That(json, Does.Not.Contain("annotations"));
    }
}
