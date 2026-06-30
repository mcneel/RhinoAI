using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RhMcp.Server;

// MapMcp wires a single POST endpoint at `pattern` that handles MCP-flavoured
// JSON-RPC 2.0. We don't implement the Streamable-HTTP SSE channel (the plugin
// only exposes request/response tools); a client requesting `text/event-stream`
// just gets back the JSON response inline, which every MCP client we test with
// tolerates.

public static class McpEndpointExtensions
{
    public static IEndpointConventionBuilder MapMcp(
        this IEndpointRouteBuilder endpoints, string pattern, bool filtered = false)
    {
        McpDispatcher dispatcher = new(endpoints.ServiceProvider, filtered);
        return endpoints.MapPost(pattern, dispatcher.HandleAsync);
    }
}

internal sealed class McpDispatcher
{
    
    private readonly ToolRegistry _tools;
    private readonly ResourceRegistry _resources;

    private bool Filtered { get; }

    public McpDispatcher(IServiceProvider rootServices, bool filtered)
    {
        _tools = ToolRegistry.Scan(typeof(McpDispatcher).Assembly, rootServices);
        _resources = ResourceRegistry.Scan(typeof(McpDispatcher).Assembly, rootServices);
        Filtered = filtered;
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        ILogger? logger = ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RhMcp.Server");

        JsonRpcRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                ctx.Request.Body, McpSerializer.Options, ctx.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCode.ParseError, Message = ex.Message }
            }).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(request?.Method))
        {
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Id = request?.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCode.InvalidRequest, Message = "Missing method." }
            }).ConfigureAwait(false);
            return;
        }

        // Notifications carry no id and expect no response (per JSON-RPC 2.0).
        // The only one we care about is `notifications/initialized`; everything
        // else we ignore quietly.
        bool isNotification = request.Id is null || request.Id.Value.ValueKind is JsonValueKind.Null;

        try
        {
            JsonRpcResponse response = await DispatchAsync(request, ctx.RequestServices, ctx.RequestAborted)
                .ConfigureAwait(false);

            // JSON-RPC 2.0: a notification gets no reply — not even an error.
            // Unhandled notification methods fall to DispatchAsync's default arm
            // and produce a MethodNotFound response; swallow it rather than
            // sending an illegal reply to a notification.
            if (isNotification)
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            response.Id = request.Id;
            await WriteResponseAsync(ctx, response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "MCP dispatch failed for method {Method}", request.Method);
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCode.InternalError,
#if DEBUG
                    Message = $"{ex.GetType().FullName}: {ex.Message}"
#else
                    Message = "Internal error."
#endif
                }
            }).ConfigureAwait(false);
        }
    }

    private Task<JsonRpcResponse> DispatchAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct) =>
        request.Method switch
        {
            "initialize" => HandleInitialize(),
            "notifications/initialized" or "notifications/cancelled" => HandleNotification(),
            "ping" => HandlePing(),
            "tools/list" => HandleToolsList(),
            "tools/call" => HandleToolCallAsync(request, services, ct),
            "resources/list" => HandleResourcesList(),
            "resources/templates/list" => HandleResourceTemplatesList(),
            "resources/read" => HandleResourceReadAsync(request, services, ct),
            _ => HandleUnknownMethod(request.Method),
        };

    private Task<JsonRpcResponse> HandleInitialize() =>
        Task.FromResult(new JsonRpcResponse
        {
            Result = new InitializeResult
            {
                ServerInfo = new ServerInfo { Name = "rhino-mcp", Version = typeof(McpDispatcher).Assembly.GetName().Version?.ToString() ?? "0.0.0" },
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability(),
                    Resources = _resources.All.Count > 0 ? new ResourcesCapability() : null,
                },
            },
        });

    // Real notifications are 204'd before serialization, so this empty result is
    // only emitted if a client wrongly sends one of these methods *with* an id —
    // then it's a request and the response must carry result|error to be legal.
    private static Task<JsonRpcResponse> HandleNotification() =>
        Task.FromResult(new JsonRpcResponse { Result = new { } });

    private static Task<JsonRpcResponse> HandlePing() =>
        Task.FromResult(new JsonRpcResponse { Result = new { } });

    private Task<JsonRpcResponse> HandleToolsList()
    {
        HashSet<string> disabled = Filtered
            ? new HashSet<string>(RhMcp.AISettings.DisabledTools, StringComparer.OrdinalIgnoreCase)
            : [];
        // In-panel-only tools (e.g. ask_user) are hidden from the external `/`
        // endpoint; only the in-panel `/agent` endpoint (Filtered) lists them.
        return Task.FromResult(new JsonRpcResponse
        {
            Result = new ListToolsResult
            {
                Tools = _tools.All
                    .Where(t => Filtered || !t.InPanelOnly)
                    .Where(t => !disabled.Contains(t.Name))
                    .Select(t => new ToolDescriptor
                {
                    Name = t.Name,
                    Title = t.Title,
                    Description = t.Description,
                    InputSchema = t.InputSchema,
                    Annotations = new ToolAnnotations
                    {
                        Title = t.Title,
                        ReadOnlyHint = t.ReadOnly,
                        DestructiveHint = t.Destructive,
                    },
                }).ToList(),
            },
        });
    }

    private Task<JsonRpcResponse> HandleResourcesList() =>
        Task.FromResult(new JsonRpcResponse
        {
            Result = new ListResourcesResult
            {
                Resources = _resources.StaticResources.Select(r => new ResourceDescriptor
                {
                    Uri = r.UriTemplate,
                    Name = r.Name,
                    Description = r.Description,
                    MimeType = r.MimeType,
                }).ToList(),
            },
        });

    private Task<JsonRpcResponse> HandleResourceTemplatesList() =>
        Task.FromResult(new JsonRpcResponse
        {
            Result = new ListResourceTemplatesResult
            {
                ResourceTemplates = _resources.Templated.Select(r => new ResourceTemplateDescriptor
                {
                    UriTemplate = r.UriTemplate,
                    Name = r.Name,
                    Description = r.Description,
                    MimeType = r.MimeType,
                }).ToList(),
            },
        });

    private static Task<JsonRpcResponse> HandleUnknownMethod(string method) =>
        Task.FromResult(new JsonRpcResponse
        {
            Error = new JsonRpcError
            {
                Code = JsonRpcErrorCode.MethodNotFound,
                Message = $"Method '{method}' is not implemented by this server.",
            },
        });

    private async Task<JsonRpcResponse> HandleToolCallAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct)
    {
        CallToolRequestParams? p = request.Params is { } pe
            ? JsonSerializer.Deserialize<CallToolRequestParams>(pe.GetRawText(), McpSerializer.Options)
            : null;

        if (p is null || string.IsNullOrEmpty(p.Name))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCode.InvalidParams, Message = "Missing tool name." }
            };

        if (!_tools.TryGet(p.Name, out ToolHandler tool))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCode.MethodNotFound,
                    Message = $"Tool '{p.Name}' is not registered.",
                }
            };

        // In-panel-only tools refuse external (`/`) callers with a plain result
        // rather than a transport error: the call "ran" and told the caller why
        // it cannot help, so an external agent can recover instead of erroring.
        if (!Filtered && tool.InPanelOnly)
            return new JsonRpcResponse
            {
                Result = new CallToolResult
                {
                    Content =
                    {
                        ContentBlock.CreateText(
                            $"'{p.Name}' is only available to the in-Rhino AI panel agent; "
                            + "use your own client's question UI."),
                    },
                }
            };

        if (Filtered && RhMcp.AISettings.DisabledTools.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCode.MethodNotFound,
                    Message = $"Tool '{p.Name}' is not available.",
                }
            };

        try
        {
            CallToolResult result = await tool.InvokeAsync(p.Arguments, services, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Result = result };
        }
        catch (Exception ex)
        {
            // Tool errors are returned as a CallToolResult with isError=true
            // rather than a JSON-RPC error — the protocol distinguishes between
            // "tool ran and failed" (data) and "tool didn't run" (transport).
            return new JsonRpcResponse
            {
                Result = new CallToolResult
                {
                    IsError = true,
                    Content = { new ContentBlock { Type = "text", Text = FormatToolError(ex) } },
                }
            };
        }
    }

    private string FormatToolError(Exception ex) =>
#if DEBUG
            $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
#else
            $"{ex.GetType().Name}: {ex.Message}";
#endif

    private async Task<JsonRpcResponse> HandleResourceReadAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct)
    {
        ReadResourceRequestParams? p = request.Params is { } pe
            ? JsonSerializer.Deserialize<ReadResourceRequestParams>(pe.GetRawText(), McpSerializer.Options)
            : null;

        if (p is null || string.IsNullOrEmpty(p.Uri))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCode.InvalidParams, Message = "Missing resource URI." }
            };

        ResourceHandler? handler = _resources.Match(p.Uri, out IReadOnlyDictionary<string, string> variables);
        if (handler is null)
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCode.MethodNotFound,
                    Message = $"No resource matches URI '{p.Uri}'.",
                }
            };

        ReadResourceResult result = await handler.InvokeAsync(p.Uri, variables, services, ct).ConfigureAwait(false);
        return new JsonRpcResponse { Result = result };
    }

    private static async Task WriteResponseAsync(HttpContext ctx, JsonRpcResponse response)
    {
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, response, McpSerializer.Options, ctx.RequestAborted)
            .ConfigureAwait(false);
    }
}
