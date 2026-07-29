using System.Text.Json.Serialization;

namespace RhMcp.Server.GhBridge;

// ---------------------------------------------------------------------------
// BRIDGE SEAM (added, not upstream).
//
// This file and GhBridgePipeClient.cs are a DELIBERATE VENDORED DUPLICATE of the
// ExGhMCP bridge wire contract. The upstream source of truth lives in
// the ExGhMCP repository:
//
//   src/ExGhMCP.Protocol/Constants/BridgeProtocolConstants.cs   (this file)
//   src/ExGhMCP.Protocol/Transport/PipeFraming.cs              (the framing)
//   src/ExGhMCP.Protocol/Protocol/*.cs                         (the DTOs)
//
// They are copied rather than project-referenced on purpose: this fork must build
// and ship from a standalone clone, with zero project references and zero shared
// assemblies. After this, the only coupling between the two repositories is the
// pipe-name string below.
//
// Drift is possible and nothing enforces agreement. `ping` returns the server's
// protocolVersion, so a mismatch against ProtocolVersion here is the signal that
// this copy has fallen behind.
// ---------------------------------------------------------------------------

/// <summary>
/// Constants and DTOs for the ExGhMCP named-pipe bridge, hosted by that
/// project's Rhino plug-in (<c>.rhp</c>) and reached by the gateway tools in
/// <c>Tools/GhBridgeTools.cs</c>.
/// </summary>
internal static class GhBridgeProtocol
{
    /// <summary>The named-pipe identity both ends agree on. The whole coupling.</summary>
    public const string PipeName = "exgh-mcp-bridge";

    /// <summary>Length in bytes of the big-endian length-prefix frame header.</summary>
    public const int FrameHeaderBytes = 4;

    /// <summary>
    /// Wire-contract version this client was written against. The server reports its
    /// own via <c>ping</c>; a mismatch means this vendored copy needs revisiting.
    /// </summary>
    public const int ProtocolVersion = 2;

    /// <summary>Overrides the per-request timeout, in milliseconds.</summary>
    public const string TimeoutEnvVar = "RHINO_MCP_GH_BRIDGE_TIMEOUT_MS";

    /// <summary>
    /// Default per-request timeout. Deliberately generous: the server loads Grasshopper
    /// on demand and budgets up to 120 s for the library scan on a plugin-heavy machine,
    /// so anything near an MCP client's usual 30-60 s would time out a legitimate first
    /// call. It must stay above that cold-load budget.
    /// </summary>
    public const int DefaultTimeoutMs = 180_000;

    /// <summary>
    /// How long to wait for the pipe to accept a connection. Much shorter than the
    /// request budget on purpose: a listening server accepts immediately, so a long wait
    /// here only delays the "plug-in not installed" answer. Capped by the request budget.
    /// </summary>
    public const int ConnectTimeoutMs = 10_000;

    /// <summary>What to tell the caller when nothing is listening on the pipe.</summary>
    public const string MissingBridgeHint =
        "Install/enable the ExGhMCP Rhino plug-in — it hosts the tool bridge.";

    /// <summary>Bridge method names.</summary>
    public static class Methods
    {
        public const string Ping = "ping";
        public const string ListTools = "list_tools";
        public const string CallTool = "call_tool";
    }

    /// <summary>Serializer settings for the bridge envelope. Kept local — no shared options.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Resolves the per-request timeout from <see cref="TimeoutEnvVar"/>, falling back to
    /// <see cref="DefaultTimeoutMs"/> for an unset, unparseable or non-positive value.
    /// </summary>
    public static int ResolveTimeoutMs()
    {
        string? raw = Environment.GetEnvironmentVariable(TimeoutEnvVar);
        if (!string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, out int parsed)
            && parsed > 0)
            return parsed;

        return DefaultTimeoutMs;
    }
}

/// <summary>A bridge request envelope. Mirrors <c>Protocol/BridgeRequest.cs</c> upstream.</summary>
internal sealed class GhBridgeRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; set; }
}

/// <summary>A bridge response envelope. Mirrors <c>Protocol/BridgeResponse.cs</c> upstream.</summary>
internal sealed class GhBridgeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public GhBridgeErrorInfo? Error { get; set; }
}

/// <summary>A bridge error payload. Mirrors <c>Protocol/BridgeError.cs</c> upstream.</summary>
internal sealed class GhBridgeErrorInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// The server answered, but with an <c>error</c> envelope. Distinct from
/// <see cref="TimeoutException"/> / <see cref="System.IO.IOException"/>, which mean the
/// bridge could not be reached at all — only the latter warrants the install hint.
/// </summary>
internal sealed class GhBridgeException : Exception
{
    public GhBridgeException(string message, int code = 0)
        : base(message) => Code = code;

    public int Code { get; }
}
