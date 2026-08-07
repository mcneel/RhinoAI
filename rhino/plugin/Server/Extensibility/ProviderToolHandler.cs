using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// A tool contributed at run time by another Rhino plug-in. Presents the same surface to the
/// dispatcher as a compiled tool; the call crosses into the contributing plug-in as a JSON
/// string and comes back as one.
/// </summary>
internal sealed class ProviderToolHandler : IMcpTool
{
    private readonly Func<string, CancellationToken, Task<string>> _handler;
    private readonly bool _marshalToUi;

    /// <summary>
    /// The contributing plug-in, as it identified itself when registering.
    /// </summary>
    public string Owner { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string? Title { get; }

    /// <inheritdoc/>
    public string? Description { get; }

    /// <inheritdoc/>
    public bool ReadOnly { get; }

    /// <inheritdoc/>
    public bool Destructive { get; }

    /// <summary>
    /// Always false: a contributing plug-in cannot register an in-panel-only tool.
    /// </summary>
    public bool InPanelOnly => false;

    /// <inheritdoc/>
    public JsonElement InputSchema { get; }

    /// <summary>
    /// The descriptor this tool was registered with, retained verbatim so
    /// <c>_router_list_contributed_tools</c> can serialize it back out — the reply is then the
    /// contributing plug-in's own declaration, not a re-projection of it.
    /// </summary>
    public ProviderToolDescriptor Descriptor { get; }

    /// <summary>
    /// Binds a descriptor and its handler into something the dispatcher can call.
    /// </summary>
    /// <param name="descriptor">Validated metadata supplied by the contributing plug-in.</param>
    /// <param name="handler">Receives the arguments as JSON text and returns the result as JSON text.</param>
    public ProviderToolHandler(ProviderToolDescriptor descriptor, Func<string, CancellationToken, Task<string>> handler)
    {
        _handler = handler;
        _marshalToUi = descriptor.RequiresUiThread;

        Descriptor = descriptor;
        Owner = descriptor.Owner;
        Name = descriptor.Name;
        Title = descriptor.Title;
        Description = descriptor.Description;
        ReadOnly = descriptor.ReadOnly;
        Destructive = descriptor.Destructive;
        InputSchema = descriptor.InputSchema;
    }

    /// <inheritdoc/>
    public Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct) =>
        UiThreadDispatch.RunAsync(_marshalToUi, () => InvokeCoreAsync(arguments, ct));

    private async Task<CallToolResult> InvokeCoreAsync(
        IDictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        string argumentsJson = SerializeArguments(arguments);

        string raw;
        try
        {
            raw = await _handler(argumentsJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never let the exception object itself escape: its type may come from the
            // contributing plug-in's AssemblyLoadContext, and the dispatcher's error
            // path should not be handling types it cannot reason about. A failed tool
            // is data, not a transport error, so this comes back as a result.
            return new CallToolResult
            {
                IsError = true,
                Content = { ContentBlock.CreateText($"{Owner}/{Name} failed -- {ex.GetType().Name}: {ex.Message}") },
            };
        }

        return Interpret(raw);
    }

    // JsonElement must not cross the boundary (see McpExtensionHost's remarks), so
    // rebuild the arguments as text. Each value keeps its raw text, so numbers,
    // nested objects and arrays survive byte for byte.
    private static string SerializeArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";

        JsonObject obj = new();
        foreach (KeyValuePair<string, JsonElement> pair in arguments)
            obj[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());

        return obj.ToJsonString();
    }

    // A handler may return either an MCP result object or any other string. Anything
    // we cannot read as the former becomes a single text block -- which is also what
    // this plug-in's own FormatResult does for a plain string return.
    internal static CallToolResult Interpret(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new CallToolResult { Content = { ContentBlock.CreateText("") } };

        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw!);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.Array)
            {
                CallToolResult result = new()
                {
                    IsError = root.TryGetProperty("isError", out JsonElement isError)
                              && isError.ValueKind == JsonValueKind.True,
                };

                foreach (JsonElement block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("text", out JsonElement text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        result.Content.Add(ContentBlock.CreateText(text.GetString() ?? ""));
                    }
                }

                if (result.Content.Count > 0)
                    return result;
            }
        }
        catch (JsonException)
        {
            // Not JSON, or not our shape. Fall through to the plain text block.
        }

        return new CallToolResult { Content = { ContentBlock.CreateText(raw!) } };
    }
}
