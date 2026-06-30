using System;
using System.Text.Json;

namespace Acp;

/// <summary>
/// Raised when a peer returns a JSON-RPC error in response to a request. Carries the wire code and
/// optional data so callers can branch on protocol error codes.
/// </summary>
public sealed class AcpException : Exception
{
    public int Code { get; }
    public JsonElement? ErrorData { get; }

    public AcpException(string message, int code = (int)JsonRpcErrorCode.InternalError, JsonElement? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    internal AcpException(JsonRpcError error) : base(error.Message)
    {
        Code = error.Code;
        ErrorData = error.Data;
    }
}
