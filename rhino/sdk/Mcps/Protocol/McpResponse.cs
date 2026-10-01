using System.Net;
using System.Text.Json.Nodes;

namespace Rhino.AI.Mcps;

internal sealed record McpResponse(HttpStatusCode Status, JsonNode? Body)
{

    public static McpResponse Json(JsonNode body, HttpStatusCode status = HttpStatusCode.OK) => new(status, body);

    public static McpResponse Accepted() => new(HttpStatusCode.Accepted, null);

    public static McpResponse Empty(HttpStatusCode status) => new(status, null);

}
