using System.Text.Json.Nodes;

namespace Rhino.AI.Mcps;

internal static class JsonRpc
{

    public static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    public static JsonObject Error(JsonNode? id, JsonRpcErrorCode code, string message, JsonNode? data = null)
    {
        JsonObject error = new()
        {
            ["code"] = (int)code,
            ["message"] = message,
        };
        if (data is not null) error["data"] = data;

        return new JsonObject()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error,
        };
    }

}
