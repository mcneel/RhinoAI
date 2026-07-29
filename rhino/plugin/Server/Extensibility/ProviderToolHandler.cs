using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

// A tool contributed at run time by another Rhino plug-in. Same surface to the
// dispatcher as a compiled tool; the call crosses into the contributing plug-in as
// a JSON string and comes back as one.
internal sealed class ProviderToolHandler : ToolHandler
{
    private readonly Func<string, CancellationToken, Task<string>> _handler;

    public string Owner { get; }

    public ProviderToolHandler(ProviderToolDescriptor descriptor, Func<string, CancellationToken, Task<string>> handler)
        : base(
            descriptor.Name, descriptor.Title, descriptor.Description,
            descriptor.ReadOnly, descriptor.Destructive,
            marshalToUi: descriptor.RequiresUiThread,
            inPanelOnly: false)
    {
        _handler = handler;
        Owner = descriptor.Owner;
        InputSchema = descriptor.InputSchema;
    }

    protected override async Task<CallToolResult> InvokeCoreAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
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
