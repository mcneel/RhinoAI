using System.Text.Json;
using NUnit.Framework;
using RhMcp.Server;

namespace RhMcp.Server.Tests;

[TestFixture]
internal class ProtocolTests
{
    private static JsonElement Serialize<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, McpSerializer.Options);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Test]
    public void Response_uses_camelCase_property_names()
    {
        JsonRpcResponse response = new()
        {
            Id = JsonDocument.Parse("1").RootElement,
            Result = new { ok = true },
        };
        JsonElement json = Serialize(response);
        Assert.That(json.TryGetProperty("jsonrpc", out _), Is.True);
        Assert.That(json.TryGetProperty("id", out _), Is.True);
        Assert.That(json.TryGetProperty("result", out _), Is.True);
    }

    [Test]
    public void Response_omits_result_and_error_when_both_null()
    {
        JsonRpcResponse response = new() { Id = JsonDocument.Parse("1").RootElement };
        JsonElement json = Serialize(response);
        Assert.That(json.TryGetProperty("result", out _), Is.False);
        Assert.That(json.TryGetProperty("error", out _), Is.False);
    }

    [Test]
    public void Error_serialises_code_as_integer()
    {
        JsonRpcResponse response = new()
        {
            Id = JsonDocument.Parse("\"req-1\"").RootElement,
            Error = new JsonRpcError { Code = JsonRpcErrorCode.InvalidParams, Message = "bad" },
        };
        JsonElement json = Serialize(response);
        JsonElement error = json.GetProperty("error");
        Assert.That(error.GetProperty("code").GetInt32(), Is.EqualTo(-32602));
        Assert.That(error.GetProperty("message").GetString(), Is.EqualTo("bad"));
    }

    [TestCase(JsonRpcErrorCode.ParseError, -32700)]
    [TestCase(JsonRpcErrorCode.InvalidRequest, -32600)]
    [TestCase(JsonRpcErrorCode.MethodNotFound, -32601)]
    [TestCase(JsonRpcErrorCode.InvalidParams, -32602)]
    [TestCase(JsonRpcErrorCode.InternalError, -32603)]
    public void Error_code_values_match_json_rpc_2_0_spec(JsonRpcErrorCode code, int expected)
    {
        Assert.That((int)code, Is.EqualTo(expected));
    }

    // JSON-RPC 2.0 §5: parse-error and invalid-request responses must emit
    // `"id": null` (field present, value null), not omit the field entirely.
    [Test]
    public void Response_preserves_explicit_null_id_for_parse_errors()
    {
        JsonRpcResponse response = new()
        {
            Id = null,
            Error = new JsonRpcError { Code = JsonRpcErrorCode.ParseError, Message = "parse" },
        };
        JsonElement json = Serialize(response);
        Assert.That(json.TryGetProperty("id", out JsonElement idElement), Is.True,
            "JSON-RPC 2.0 requires `id: null` on parse-error responses, not field omission");
        Assert.That(idElement.ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Request_roundtrips_through_serializer()
    {
        const string wire = """
        { "jsonrpc": "2.0", "id": 7, "method": "tools/list" }
        """;
        JsonRpcRequest? request = JsonSerializer.Deserialize<JsonRpcRequest>(wire, McpSerializer.Options);
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Jsonrpc, Is.EqualTo("2.0"));
        Assert.That(request.Method, Is.EqualTo("tools/list"));
        Assert.That(request.Id, Is.Not.Null);
        Assert.That(request.Id!.Value.GetInt32(), Is.EqualTo(7));
    }

    [Test]
    public void Notification_has_null_id_after_deserialise()
    {
        const string wire = """
        { "jsonrpc": "2.0", "method": "notifications/initialized" }
        """;
        JsonRpcRequest? request = JsonSerializer.Deserialize<JsonRpcRequest>(wire, McpSerializer.Options);
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.Id, Is.Null);
    }

    [Test]
    public void ContentBlock_CreateText_emits_text_type()
    {
        ContentBlock block = ContentBlock.CreateText("hello");
        JsonElement json = Serialize(block);
        Assert.That(json.GetProperty("type").GetString(), Is.EqualTo("text"));
        Assert.That(json.GetProperty("text").GetString(), Is.EqualTo("hello"));
        Assert.That(json.TryGetProperty("data", out _), Is.False);
        Assert.That(json.TryGetProperty("mimeType", out _), Is.False);
    }

    [Test]
    public void ContentBlock_CreateImage_from_bytes_base64_encodes()
    {
        byte[] bytes = { 0x89, 0x50, 0x4e, 0x47 };
        ContentBlock block = ContentBlock.CreateImage(bytes, "image/png");
        JsonElement json = Serialize(block);
        Assert.That(json.GetProperty("type").GetString(), Is.EqualTo("image"));
        Assert.That(json.GetProperty("data").GetString(), Is.EqualTo(System.Convert.ToBase64String(bytes)));
        Assert.That(json.GetProperty("mimeType").GetString(), Is.EqualTo("image/png"));
        Assert.That(json.TryGetProperty("text", out _), Is.False);
    }

    // Boundary case: an empty byte[] should produce an empty `data` string,
    // not throw, and not omit the property — tools that yield a zero-byte
    // placeholder image should still emit a well-formed image block.
    [Test]
    public void ContentBlock_CreateImage_with_empty_bytes_emits_empty_data()
    {
        ContentBlock block = ContentBlock.CreateImage(System.Array.Empty<byte>(), "image/png");
        JsonElement json = Serialize(block);
        Assert.That(json.GetProperty("type").GetString(), Is.EqualTo("image"));
        Assert.That(json.GetProperty("data").GetString(), Is.EqualTo(""));
        Assert.That(json.GetProperty("mimeType").GetString(), Is.EqualTo("image/png"));
    }

    [Test]
    public void CallToolResult_omits_isError_when_false()
    {
        CallToolResult result = new() { IsError = false };
        result.Content.Add(ContentBlock.CreateText("done"));
        JsonElement json = Serialize(result);
        Assert.That(json.TryGetProperty("isError", out _), Is.False,
            "default-false IsError is suppressed so non-error tools emit a tighter payload");
    }

    [Test]
    public void CallToolResult_includes_isError_when_true()
    {
        CallToolResult result = new() { IsError = true };
        result.Content.Add(ContentBlock.CreateText("boom"));
        JsonElement json = Serialize(result);
        Assert.That(json.GetProperty("isError").GetBoolean(), Is.True);
    }

    [Test]
    public void InitializeResult_pins_protocol_version_literal()
    {
        InitializeResult init = new();
        JsonElement json = Serialize(init);
        Assert.That(json.GetProperty("protocolVersion").GetString(), Is.EqualTo("2024-11-05"));
    }

    [Test]
    public void ServerCapabilities_omits_unset_optional_capabilities()
    {
        ServerCapabilities caps = new();
        JsonElement json = Serialize(caps);
        Assert.That(json.TryGetProperty("tools", out _), Is.False);
        Assert.That(json.TryGetProperty("resources", out _), Is.False);
    }

    [Test]
    public void ResourceContent_omits_missing_optional_payloads()
    {
        ResourceContent content = new() { Uri = "rhino://about", Text = "hi" };
        JsonElement json = Serialize(content);
        Assert.That(json.GetProperty("uri").GetString(), Is.EqualTo("rhino://about"));
        Assert.That(json.GetProperty("text").GetString(), Is.EqualTo("hi"));
        Assert.That(json.TryGetProperty("blob", out _), Is.False);
        Assert.That(json.TryGetProperty("mimeType", out _), Is.False);
    }
}
