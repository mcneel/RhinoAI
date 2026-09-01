using Rhino.Compute;
using RhMcp.Compute;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ComputeGrasshopperTool
{
    [McpServerTool(Name = "compute_grasshopper")]
    [BackgroundThread]
    [Description("Solve a Grasshopper definition (.gh / .ghx) on a Rhino Compute server (NOT in the current Rhino doc). `definition` is either a URL or an absolute file path readable by the plugin process. `inputs` is a JSON object mapping input parameter names (e.g. \"RH_IN:radius\") to an array of primitive values placed at path {0}. Returns JSON with serverUrl, outputs (array of { paramName, branches }), and error fields. Server URL comes from RHINO_COMPUTE_URL (defaults to http://localhost:6500).")]
    public static string ComputeGrasshopper(
        [Description("Definition source: an http(s) URL to a hosted .gh/.ghx, or an absolute path to a local file.")] string definition,
        [Description("Optional JSON object: { \"<paramName>\": [v1, v2, ...] }. Supported value types: number, bool, string.")] string? inputs = null)
    {
        ComputeConfig.EnsureInitialized();
        if (ComputeDiagnostics.IsMacOs() && !ComputeConfig.HasCustomUrl)
            return UnreachableError("Rhino Compute does not run on macOS.");

        List<GrasshopperDataTree> trees;
        try
        {
            trees = BuildInputTrees(inputs);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs = (object?)null, error = $"Bad inputs JSON: {ex.Message}" });
        }

        try
        {
            var resultTrees = GrasshopperCompute.EvaluateDefinition(definition, trees);
            var outputs = resultTrees?.Select(TreeToObject).ToArray() ?? Array.Empty<object>();
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs, error = (string?)null });
        }
        catch (Exception ex) when (ComputeDiagnostics.IsConnectionFailure(ex))
        {
            return UnreachableError($"Could not reach compute server: {ex.Message}");
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs = (object?)null, error = ex.Message });
        }
    }

    private static string UnreachableError(string message)
    {
        var status = ComputeDiagnostics.Probe();
        return JsonSerializer.Serialize(new
        {
            serverUrl = ComputeConfig.CurrentUrl,
            outputs = (object?)null,
            error = message,
            diagnostics = ComputeDiagnostics.ToDto(status),
        });
    }

    private static List<GrasshopperDataTree> BuildInputTrees(string? json)
    {
        var trees = new List<GrasshopperDataTree>();
        if (string.IsNullOrWhiteSpace(json)) return trees;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("inputs must be a JSON object");

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"input '{prop.Name}' must be an array of primitive values");

            var tree = new GrasshopperDataTree(prop.Name);
            var items = new List<GrasshopperObject>();
            foreach (var el in prop.Value.EnumerateArray())
            {
                var boxed = BoxJsonPrimitive(prop.Name, el);
                if (boxed is null) continue;
                items.Add(new GrasshopperObject(boxed));
            }
            tree.Append(items, "{0}");
            trees.Add(tree);
        }
        return trees;
    }

    private static object? BoxJsonPrimitive(string paramName, JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True or JsonValueKind.False => el.GetBoolean(),
        JsonValueKind.Number when el.TryGetInt32(out var i) => i,
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.Null => null,
        _ => throw new ArgumentException($"input '{paramName}' contains a non-primitive value of kind {el.ValueKind}"),
    };

    private static object TreeToObject(GrasshopperDataTree tree)
    {
        var branches = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        foreach (var kv in tree.InnerTree)
        {
            var items = kv.Value?.Select(ParseGhObject).ToArray() ?? Array.Empty<object?>();
            branches[kv.Key] = items;
        }
        return new { paramName = tree.ParamName, branches };
    }

    // GrasshopperObject.Data is already JSON — parse it to a JsonElement so the
    // outer JsonSerializer.Serialize doesn't double-encode it as a string. If parsing
    // fails for any reason, fall back to the raw string plus the reported type so the
    // agent at least sees something.
    private static object? ParseGhObject(GrasshopperObject obj)
    {
        if (obj is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(obj.Data ?? "null");
            return new { type = obj.Type, value = doc.RootElement.Clone() };
        }
        catch
        {
            return new { type = obj.Type, raw = obj.Data };
        }
    }
}
