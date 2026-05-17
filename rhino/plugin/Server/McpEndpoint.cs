using System.IO;
using System.Reflection;
using System.Text.Json;
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
        this IEndpointRouteBuilder endpoints, string pattern, McpEndpointOptions options)
    {
        var dispatcher = new McpDispatcher(options, endpoints.ServiceProvider);
        return endpoints.MapPost(pattern, dispatcher.HandleAsync);
    }
}

public sealed class McpEndpointOptions
{
    public string ServerName { get; set; } = "rhino-mcp";
    public string ServerVersion { get; set; } = "0.1.0";
    public Assembly ToolAssembly { get; set; } = typeof(McpEndpointOptions).Assembly;
    public bool SurfaceExceptionDetailsToClient { get; set; }
}

internal sealed class McpDispatcher
{
    private readonly McpEndpointOptions _options;
    private readonly ToolRegistry _tools;
    private readonly ResourceRegistry _resources;

    public McpDispatcher(McpEndpointOptions options, IServiceProvider rootServices)
    {
        _options = options;
        _tools = ToolRegistry.Scan(options.ToolAssembly, rootServices);
        _resources = ResourceRegistry.Scan(options.ToolAssembly, rootServices);
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("RhMcp.Server");

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
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.ParseError, Message = ex.Message }
            }).ConfigureAwait(false);
            return;
        }

        if (request is null || request.Method is null || string.IsNullOrEmpty(request.Method))
        {
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Id = request?.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidRequest, Message = "Missing method." }
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

            if (isNotification && response.Error is null)
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
                    Code = JsonRpcErrorCodes.InternalError,
                    Message = _options.SurfaceExceptionDetailsToClient
                        ? $"{ex.GetType().FullName}: {ex.Message}"
                        : "Internal error.",
                }
            }).ConfigureAwait(false);
        }
    }

    private async Task<JsonRpcResponse> DispatchAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "initialize":
                return new JsonRpcResponse
                {
                    Result = new InitializeResult
                    {
                        ServerInfo = new ServerInfo { Name = _options.ServerName, Version = _options.ServerVersion },
                        Capabilities = new ServerCapabilities
                        {
                            Tools = new ToolsCapability(),
                            Resources = _resources.All.Count > 0 ? new ResourcesCapability() : null,
                        },
                    },
                };

            case "notifications/initialized":
            case "notifications/cancelled":
                return new JsonRpcResponse(); // notification — no result, no error

            case "ping":
                return new JsonRpcResponse { Result = new { } };

            case "tools/list":
                return new JsonRpcResponse
                {
                    Result = new ListToolsResult
                    {
                        Tools = _tools.All.Select(t => new ToolDescriptor
                        {
                            Name = t.Name,
                            Title = t.Title,
                            Description = t.Description,
                            InputSchema = t.InputSchema,
                        }).ToList(),
                    },
                };

            case "tools/call":
                return await HandleToolCallAsync(request, services, ct).ConfigureAwait(false);

            case "resources/list":
                return new JsonRpcResponse
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
                };

            case "resources/templates/list":
                return new JsonRpcResponse
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
                };

            case "resources/read":
                return await HandleResourceReadAsync(request, services, ct).ConfigureAwait(false);

            default:
                return new JsonRpcResponse
                {
                    Error = new JsonRpcError
                    {
                        Code = JsonRpcErrorCodes.MethodNotFound,
                        Message = $"Method '{request.Method}' is not implemented by this server.",
                    },
                };
        }
    }

    private async Task<JsonRpcResponse> HandleToolCallAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct)
    {
        CallToolRequestParams? p = request.Params is { } pe
            ? JsonSerializer.Deserialize<CallToolRequestParams>(pe.GetRawText(), McpSerializer.Options)
            : null;

        if (p is null || string.IsNullOrEmpty(p.Name))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing tool name." }
            };

        if (!_tools.TryGet(p.Name, out var tool))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"Tool '{p.Name}' is not registered.",
                }
            };

        try
        {
            var result = await tool.InvokeAsync(p.Arguments, services, ct).ConfigureAwait(false);
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
        _options.SurfaceExceptionDetailsToClient
            ? $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"
            : $"{ex.GetType().Name}: {ex.Message}";

    private async Task<JsonRpcResponse> HandleResourceReadAsync(
        JsonRpcRequest request, IServiceProvider services, CancellationToken ct)
    {
        ReadResourceRequestParams? p = request.Params is { } pe
            ? JsonSerializer.Deserialize<ReadResourceRequestParams>(pe.GetRawText(), McpSerializer.Options)
            : null;

        if (p is null || string.IsNullOrEmpty(p.Uri))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing resource URI." }
            };

        var handler = _resources.Match(p.Uri, out var variables);
        if (handler is null)
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"No resource matches URI '{p.Uri}'.",
                }
            };

        var result = await handler.InvokeAsync(p.Uri, variables, services, ct).ConfigureAwait(false);
        return new JsonRpcResponse { Result = result };
    }

    private static async Task WriteResponseAsync(HttpContext ctx, JsonRpcResponse response)
    {
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, response, McpSerializer.Options, ctx.RequestAborted)
            .ConfigureAwait(false);
    }
}
