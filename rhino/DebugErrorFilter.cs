using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RhMcp;

internal static class DebugErrorFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
        next => async (request, ct) =>
        {
            try
            {
                return await next(request, ct);
            }
            catch (Exception ex)
            {
                var toolName = request.Params?.Name ?? "<unknown>";
                RhinoApp.WriteLine($"[Rhino MCP] Tool '{toolName}' threw — surfacing to client (Debug build).");
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = $"Tool '{toolName}' threw {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"
                        }
                    ]
                };
            }
        };
}
