using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI.Mcps;

// Plain data so McpProtocol never touches HttpListener types and can be tested with strings.
internal sealed record McpHttpRequest(string HttpMethod, string? ProtocolVersionHeader, string? Origin, string Body)
{

    internal static async Task<McpHttpRequest> FromRequestAsync(HttpListenerRequest request, CancellationToken token)
    {
        using StreamReader reader = new(request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync(token).ConfigureAwait(false);

        return new McpHttpRequest(
            request.HttpMethod,
            request.Headers["MCP-Protocol-Version"],
            request.Headers["Origin"],
            body);
    }

}
