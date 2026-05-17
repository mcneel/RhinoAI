using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RhMcp.Server;

// JSON-RPC 2.0 envelopes + the subset of MCP protocol types we actually emit.
// All public fields use camelCase via JsonNamingPolicy.CamelCase, which is the
// global default set on McpSerializer.Options.

internal sealed class JsonRpcRequest
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }
    public string Method { get; set; } = "";
    public JsonElement? Params { get; set; }
}

internal sealed class JsonRpcResponse
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

internal sealed class JsonRpcError
{
    public JsonRpcErrorCode Code { get; set; }
    public string Message { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }
}

internal enum JsonRpcErrorCode
{
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,
}

// ----- MCP-level types --------------------------------------------------------

internal sealed class InitializeResult
{
    public string ProtocolVersion { get; set; } = "2024-11-05";
    public ServerInfo ServerInfo { get; set; } = new();
    public ServerCapabilities Capabilities { get; set; } = new();
}

internal sealed class ServerInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

internal sealed class ServerCapabilities
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolsCapability? Tools { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResourcesCapability? Resources { get; set; }
}

internal sealed class ToolsCapability
{
    public bool ListChanged { get; set; }
}

internal sealed class ResourcesCapability
{
    public bool Subscribe { get; set; }
    public bool ListChanged { get; set; }
}

internal sealed class ToolDescriptor
{
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    public JsonElement InputSchema { get; set; }
}

internal sealed class ListToolsResult
{
    public List<ToolDescriptor> Tools { get; set; } = new();
}

internal sealed class CallToolRequestParams
{
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, JsonElement>? Arguments { get; set; }
}

internal sealed class CallToolResult
{
    public List<ContentBlock> Content { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; set; }
}

// Public so tools that need to return mixed content (e.g. a text message plus
// an inline image) can yield ContentBlock instances directly. Tools that just
// return a string never have to touch this type.
public sealed class ContentBlock
{
    public string Type { get; set; } = "text";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    public static ContentBlock CreateText(string text) =>
        new() { Type = "text", Text = text };

    public static ContentBlock CreateImage(byte[] data, string mimeType) =>
        new() { Type = "image", Data = Convert.ToBase64String(data), MimeType = mimeType };

    public static ContentBlock CreateImage(string base64Data, string mimeType) =>
        new() { Type = "image", Data = base64Data, MimeType = mimeType };
}

internal sealed class ResourceDescriptor
{
    public string Uri { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
}

internal sealed class ResourceTemplateDescriptor
{
    public string UriTemplate { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
}

internal sealed class ListResourcesResult
{
    public List<ResourceDescriptor> Resources { get; set; } = new();
}

internal sealed class ListResourceTemplatesResult
{
    public List<ResourceTemplateDescriptor> ResourceTemplates { get; set; } = new();
}

internal sealed class ReadResourceRequestParams
{
    public string Uri { get; set; } = "";
}

internal sealed class ReadResourceResult
{
    public List<ResourceContent> Contents { get; set; } = new();
}

internal sealed class ResourceContent
{
    public string Uri { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blob { get; set; }
}

// ----- Shared serializer options ---------------------------------------------

internal static class McpSerializer
{
    // WebApplication.CreateSlimBuilder turns off
    // JsonSerializer.IsReflectionEnabledByDefault, which makes JsonNode.ToJsonString
    // and JsonSerializer.Serialize throw unless the options expose a TypeInfoResolver.
    // DefaultJsonTypeInfoResolver brings the reflection path back in — we're not
    // building for AOT/trim, we just inherited the slim host's defaults.
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
