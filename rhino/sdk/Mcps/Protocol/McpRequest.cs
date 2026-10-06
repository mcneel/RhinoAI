using System.Text.Json.Nodes;

namespace Rhino.AI.Mcps;

internal sealed record McpRequest(JsonNode? Id, string Method, JsonObject? Params, string ProtocolVersion)
{

    public bool IsNotification => Id is null;


}
