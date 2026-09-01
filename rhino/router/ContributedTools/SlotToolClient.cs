using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace RhMcp.Router;

/// <summary>
/// Asks one Rhino slot which tools were contributed to it at run time.
/// </summary>
/// <remarks>
/// <para>
/// The question is asked through the plugin's private <c>_router_list_contributed_tools</c>
/// control tool, which reads <c>McpExtensionRegistry</c> exactly — so the answer contains only
/// runtime-registered tools, never the plugin's compiled ones. The router used to pull the
/// slot's full <c>tools/list</c> and subtract its own compiled names, and that inference leaked:
/// a compiled tool the router's build had excluded on purpose (GH2_* on an R8 router) looked
/// identical to a contributed one.
/// </para>
/// <para>
/// Never throws. A slot that is unreachable, wedged, mid-shutdown, answering with malformed
/// JSON, or running a plugin build old enough to lack the control tool contributes nothing; it
/// must not be able to fail the whole listing. That is deliberate degradation: a whole slot's
/// tools simply do not appear, which can never mis-classify a compiled tool.
/// </para>
/// </remarks>
internal sealed class SlotToolClient(IHttpClientFactory httpFactory, ILogger<SlotToolClient> log)
{
    /// <summary>
    /// Per-slot budget. HttpClient's default timeout is 100 seconds, and this runs on the
    /// client's connect path: a Rhino that is wedged rather than dead would stall
    /// <c>tools/list</c> for all of it. The control tool is marked <c>[BackgroundThread]</c> and
    /// reads one concurrent dictionary — no UI-thread marshalling — so it stays fast even
    /// mid-solve, and this is generous for that.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    private const string ControlToolName = "_router_list_contributed_tools";

    /// <summary>The control call never varies, so it is serialized once.</summary>
    private static readonly string ListContributedRequest = JsonSerializer.Serialize(
        new JsonRpcRequest(
            Jsonrpc: "2.0",
            Id: "1",
            Method: "tools/call",
            Params: new JsonRpcRequestParams(ControlToolName, new JsonObject())),
        RouterJsonContext.Default.JsonRpcRequest);

    /// <summary>
    /// The tools contributed to <paramref name="slot"/> at run time, or an empty list when the
    /// slot cannot or does not answer.
    /// </summary>
    public async Task<IReadOnlyList<Tool>> ListAsync(ChildRhino slot, CancellationToken cancellationToken)
    {
        try
        {
            HttpClient http = httpFactory.CreateClient();
          
            http.Timeout = Timeout;

            using StringContent content = new(ListContributedRequest, Encoding.UTF8, "application/json");
            
            using HttpRequestMessage message = new(HttpMethod.Post, slot.Endpoint + "/") { Content = content };

            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using HttpResponseMessage response = await http
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return [];

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Shared with the call path, so bare-JSON and SSE are unwrapped one way only. Throws
            // on a JSON-RPC error -- which is what an old plugin without the control tool sends --
            // and the catch below turns that into an empty list.
            JsonElement result = ProxyDispatcher.ExtractMcpResult(body, slot.SlotId, ControlToolName);

            return this.Parse(result);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Slot '{Slot}' did not answer {Tool}", slot.SlotId, ControlToolName);
            return [];
        }
    }

    /// <summary>
    /// Reads the tool array out of the control tool's reply: a <see cref="CallToolResult"/>
    /// whose first text block carries the plugin's descriptor array as JSON, exactly as the
    /// plugin's tool host wraps every string return.
    /// </summary>
    private IReadOnlyList<Tool> Parse(JsonElement result)
    {
        var call = (CallToolResult?)result.Deserialize(
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolResult)));

        if (call is null || call.IsError is true|| 
            call.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text is not { Length: > 0 } payload)
            return [];

        var tools = (IList<Tool>?)JsonSerializer.Deserialize(
            payload, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IList<Tool>)));

        return tools is null ? [] : [.. tools.Where(t => !string.IsNullOrEmpty(t.Name))];
    }
}
