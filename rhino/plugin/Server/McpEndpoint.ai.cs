using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Rhino.AI.Tools;

namespace Rhino.AI.Server;

// McpServer gives each listener prefix one dispatcher, handling MCP-flavoured
// JSON-RPC 2.0. We don't implement the Streamable-HTTP SSE channel (the plugin
// only exposes request/response tools); a client requesting `text/event-stream`
// just gets back the JSON response inline, which every MCP client we test with
// tolerates.

internal sealed class McpDispatcher
{
    private readonly ToolRegistry _tools;
    private readonly ResourceRegistry _resources;

    private IServiceProvider Services { get; }

    // Which assistant this route serves, or null for the external one. The external client has its
    // own permission UI and is never gated here; an in-Rhino panel is gated by that panel's modes.
    private AIProfile? Profile { get; }

    private bool Filtered => Profile is not null;

    public McpDispatcher(IServiceProvider rootServices, AIProfile? profile)
    {
        _tools = ToolRegistry.Scan(typeof(McpDispatcher).Assembly, rootServices);
        _resources = ResourceRegistry.Scan(typeof(McpDispatcher).Assembly, rootServices);
        Services = rootServices;
        Profile = profile;
    }

    public async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        JsonRpcRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                ctx.Request.InputStream, McpSerializer.Options, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcErrorCode.ParseError, Message = ex.Message }
            }, ct).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(request?.Method))
        {
            await WriteResponseAsync(ctx, new JsonRpcResponse
            {
                Id = request?.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCode.InvalidRequest, Message = "Missing method." }
            }, ct).ConfigureAwait(false);
            return;
        }

        // Notifications carry no id and expect no response (per JSON-RPC 2.0).
        // The only one we care about is `notifications/initialized`; everything
        // else we ignore quietly.
        bool isNotification = request.Id is null || request.Id.Value.ValueKind is JsonValueKind.Null;

        try
        {
            JsonRpcResponse response = await DispatchAsync(request, Services, ct)
                .ConfigureAwait(false);

            // JSON-RPC 2.0: a notification gets no reply — not even an error.
            // Unhandled notification methods fall to DispatchAsync's default arm
            // and produce a MethodNotFound response; swallow it rather than
            // sending an illegal reply to a notification.
            if (isNotification)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            response.Id = request.Id;
            await WriteResponseAsync(ctx, response, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[RhinoAI] MCP dispatch failed for method {request.Method}: {ex.GetType().Name}: {ex.Message}");
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
            }, ct).ConfigureAwait(false);
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
        // In-panel-only tools (e.g. ask_user) are hidden from the external `/` endpoint; only an
        // in-Rhino panel's route lists them, and only those honour that panel's per-tool modes.
        AIProfile? gated = Profile;
        return Task.FromResult(new JsonRpcResponse
        {
            Result = new ListToolsResult
            {
                Tools = _tools.All
                    .Where(t => Filtered || !t.InPanelOnly)
                    .Where(t => gated is not AIProfile profile || ToolPolicy.IsAvailable(profile, t))
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

        AIProfile? gated = Profile;

        // Which assistant is calling, for the length of this call: list_enabled has to answer for
        // the route it arrived on, and nothing else here carries that down into a tool.
        ToolAudience.Profile = gated;

        if (gated is AIProfile profile && !ToolPolicy.IsAvailable(profile, tool))
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCode.MethodNotFound,
                    Message = $"Tool '{p.Name}' is not available.",
                }
            };

        // A declined call is a result, not an error: the agent is told to stop rather than to retry,
        // and the tool is never entered. The document is what locates the conversation the request is
        // asked in; without one the policy falls back to a dialog.
        if (gated is AIProfile asking && ToolPolicy.NeedsConfirmation(asking, tool)
            && !await ToolPolicy.ConfirmAsync(asking, services.GetService(typeof(RhinoDoc)) as RhinoDoc, tool, p.Arguments, ct)
                .ConfigureAwait(false))
            return new JsonRpcResponse
            {
                Result = ToolResultFormatter.Format(Failure(
                    ToolError.Refused,
                    $"The user declined to run '{p.Name}'.",
                    "Do not retry it on your own. Explain what you wanted to do and ask the user to "
                    + "allow it; the tool is set to ask before each call in Rhino AI settings.")),
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

    private static async Task WriteResponseAsync(
        HttpListenerContext ctx, JsonRpcResponse response, CancellationToken ct)
    {
        using MemoryStream buffer = new();
        await JsonSerializer.SerializeAsync(buffer, response, McpSerializer.Options, ct).ConfigureAwait(false);

        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = buffer.Length;
        buffer.Position = 0;
        await buffer.CopyToAsync(ctx.Response.OutputStream, ct).ConfigureAwait(false);
    }
}
