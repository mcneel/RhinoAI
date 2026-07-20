using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Grasshopper.Kernel;

using GrasshopperAITools.Schema;   // SchemaBuilder, ToolExecutor, ToolResultEncoder, AIToolSchema, ToolExecutionResult
using GrasshopperAITools.Services; // ToolLoader

namespace RhMcp.Server;

// -------------------------------------------------------------------------
// BRIDGE SEAM (added, not upstream). Exposes saved Grasshopper "AI Tool"
// bundles as dynamic MCP tools, executed by the GrasshopperAITools.Pipeline
// engine. RhinoMCP's static reflection registry (ToolRegistry) knows nothing
// about these; McpDispatcher consults this source alongside it. Keeping all
// the logic here means the dispatcher diff stays tiny and upstream merges stay
// clean.
// -------------------------------------------------------------------------

/// <summary>
/// A Grasshopper definition exposed as an MCP tool: its SchemaBuilder-derived
/// input schema plus the embedded inner document that ToolExecutor runs.
/// </summary>
internal sealed record GhTool(string Name, string Description, JsonElement InputSchema, GH_Document InnerDoc);

/// <summary>
/// Dynamic tool source backed by a folder of <c>.gh</c> / <c>.xml</c> / <c>.aitool</c>
/// bundles authored and exported by the GrasshopperAITools <c>.gha</c>. Each bundle's
/// SchemaBuilder JSON becomes the tool's <c>input_schema</c>; calls execute the embedded
/// inner document via <see cref="ToolExecutor.ExecuteWithJson"/>.
/// </summary>
internal sealed class GhToolSource
{
    // Opt-in override so experiments can point at any folder; otherwise the same
    // per-user tools directory the GrasshopperAITools stack already uses.
    private const string DirEnvVar = "RHINO_MCP_GH_TOOLS_DIR";

    private readonly string _toolsDir;
    private readonly object _gate = new();
    private Dictionary<string, GhTool> _byName = new(StringComparer.OrdinalIgnoreCase);

    public GhToolSource(string toolsDir) => _toolsDir = toolsDir;

    public string ToolsDirectory => _toolsDir;

    /// <summary>Resolves the tools folder from the env override or the default per-user path.</summary>
    public static string ResolveDefaultDirectory()
    {
        string? overridden = Environment.GetEnvironmentVariable(DirEnvVar);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden!;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrasshopperAITools", "tools");
    }

    public IReadOnlyCollection<GhTool> All
    {
        get { lock (_gate) { return _byName.Values.ToArray(); } }
    }

    public bool TryGet(string name, out GhTool tool)
    {
        lock (_gate) { return _byName.TryGetValue(name, out tool!); }
    }

    /// <summary>
    /// Re-scan the tools folder. Cheap enough to call on every <c>tools/list</c>, which
    /// gives near-live pickup without needing MCP list-changed notifications (which
    /// RhinoMCP's request/response endpoint does not emit).
    /// </summary>
    public void Refresh()
    {
        var map = new Dictionary<string, GhTool>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_toolsDir))
        {
            foreach (string file in EnumerateBundles(_toolsDir))
            {
                GhTool? tool = TryLoad(file);
                if (tool != null && !map.ContainsKey(tool.Name))
                    map[tool.Name] = tool;
            }
        }
        lock (_gate) { _byName = map; }
    }

    private static IEnumerable<string> EnumerateBundles(string dir) =>
        Directory.EnumerateFiles(dir, "*.gh")
            .Concat(Directory.EnumerateFiles(dir, "*.xml"))
            .Concat(Directory.EnumerateFiles(dir, "*.aitool"));

    private static GhTool? TryLoad(string filePath)
    {
        try
        {
            bool isBundle = string.Equals(Path.GetExtension(filePath), ".aitool", StringComparison.OrdinalIgnoreCase);
            AIToolSchema schema = isBundle
                ? ToolLoader.LoadFromBundle(filePath, out _)
                : ToolLoader.LoadFromFile(filePath, out _);

            if (schema == null || schema.EmbeddedInnerDoc == null || string.IsNullOrEmpty(schema.Name))
                return null;

            // AIToolSchema.Json is the full Anthropic tool definition; MCP's tools/list
            // wants only the input_schema object.
            JsonElement inputSchema;
            using (JsonDocument doc = JsonDocument.Parse(schema.Json))
            {
                if (!doc.RootElement.TryGetProperty("input_schema", out JsonElement inEl))
                    return null;
                inputSchema = inEl.Clone();
            }

            return new GhTool(schema.Name, schema.Description ?? string.Empty, inputSchema, schema.EmbeddedInnerDoc);
        }
        catch
        {
            // A malformed bundle must not take down discovery of the others.
            return null;
        }
    }

    /// <summary>
    /// Execute a tool. The Grasshopper solution is marshalled onto the Rhino UI thread —
    /// <c>NewSolution</c> must not run off-thread (mirrors RhinoMCP's ToolHandler policy).
    /// Returns a JSON string of named outputs plus any solve messages.
    /// </summary>
    public Task<string> ExecuteAsync(GhTool tool, IDictionary<string, JsonElement>? arguments)
    {
        JsonElement argsElement = ToArgsElement(arguments);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.SetResult(Execute(tool, argsElement)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }

    /// <summary>Normalise an MCP argument map into the single JSON object ToolExecutor expects.</summary>
    public static JsonElement ToArgsElement(IDictionary<string, JsonElement>? arguments)
    {
        var argsObj = new JsonObject();
        if (arguments != null)
        {
            foreach (KeyValuePair<string, JsonElement> kv in arguments)
                argsObj[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
        }
        return JsonSerializer.SerializeToElement(argsObj);
    }

    /// <summary>
    /// Run a tool with an already-built arguments object. MUST be called on the Rhino UI
    /// thread — callers off-thread should use <see cref="ExecuteAsync"/>. (RhinoMCP's
    /// ToolHandler already marshals static [McpServerTool] calls to the UI thread, so the
    /// gateway tool can call this directly.)
    /// </summary>
    public string Execute(GhTool tool, JsonElement argsElement)
    {
        ToolExecutionResult result = ToolExecutor.ExecuteWithJson(tool.InnerDoc, argsElement, out var messages);

        var outputs = new JsonObject();
        int index = 0;
        foreach (ToolOutputEntry o in result.Outputs)
        {
            string key = !string.IsNullOrEmpty(o.Name)
                ? o.Name
                : (result.Outputs.Count == 1 ? "result" : $"output{index}");

            if (o.IsList)
            {
                var arr = new JsonArray();
                foreach (var item in o.Items)
                    arr.Add(ToolResultEncoder.EncodeSingleAsNode(item));
                outputs[key] = arr;
            }
            else
            {
                outputs[key] = ToolResultEncoder.EncodeSingleAsNode(o.First);
            }
            index++;
        }

        var payload = new JsonObject { ["outputs"] = outputs };
        if (messages != null && messages.Count > 0)
        {
            var msgs = new JsonArray();
            foreach (var (level, text) in messages)
                msgs.Add(new JsonObject { ["level"] = level.ToString(), ["text"] = text });
            payload["messages"] = msgs;
        }

        return payload.ToJsonString();
    }
}
