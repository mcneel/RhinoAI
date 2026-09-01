using Rhino.Collections;
using Rhino.Compute;
using RhMcp.Compute;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ComputePythonTool
{
    [McpServerTool(Name = "compute_python")]
    [BackgroundThread]
    [Description("Evaluate a Python 3 script on a Rhino Compute server (NOT in the current Rhino doc). Use this for headless geometry work that doesn't need the live document. `inputs` is a JSON object whose top-level fields become script variables. `outputs` is an array of variable names the script must assign — those values are returned in the result `outputs` field. Returns JSON with serverUrl, outputs, and error fields; error is null on success. Server URL comes from RHINO_COMPUTE_URL (defaults to http://localhost:6500). The compute Python sandbox only allows Rhino-namespaced imports (`import Rhino.Geometry as rg`, `import rhinoscriptsyntax as rs`) — the stdlib (`math`, `os`, `sys`), `clr`, and `from X import Y` syntax are blocked. Use literals (e.g. `pi = 3.141592653589793`) or Rhino APIs instead.")]
    public static string ComputePython(
        [Description("Python 3 script. Read inputs by name (e.g. `radius`) and assign each output variable listed in `outputs`. Only Rhino-namespaced imports are permitted; no stdlib, no `from … import …`.")] string script,
        [Description("Names of variables the script will assign and that should be returned. At least one is required.")] string[] outputs,
        [Description("Optional JSON object of input variables. Top-level fields become script inputs; supported value types: number, bool, string, null, and arrays of those.")] string? inputs = null)
    {
        ComputeConfig.EnsureInitialized();
        if (ComputeDiagnostics.IsMacOs() && !ComputeConfig.HasCustomUrl)
            return UnreachableError("Rhino Compute does not run on macOS.");

        if (outputs is null || outputs.Length == 0)
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs = (object?)null, error = "`outputs` must list at least one variable name." });

        ArchivableDictionary inDict;
        try
        {
            inDict = BuildInputs(inputs);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Bad inputs JSON: {ex.Message}", serverUrl = ComputeConfig.CurrentUrl });
        }

        try
        {
            var outDict = PythonCompute.Evaluate(script, inDict, outputs);
            var outputsDict = DictToObject(outDict);
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs = outputsDict, error = (string?)null });
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

    private static ArchivableDictionary BuildInputs(string? json)
    {
        var dict = new ArchivableDictionary();
        if (string.IsNullOrWhiteSpace(json)) return dict;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("inputs must be a JSON object");

        foreach (var prop in doc.RootElement.EnumerateObject())
            SetFromJson(dict, prop.Name, prop.Value);

        return dict;
    }

    private static void SetFromJson(ArchivableDictionary dict, string key, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                dict.Set(key, value.GetString());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                dict.Set(key, value.GetBoolean());
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var i)) dict.Set(key, i);
                else dict.Set(key, value.GetDouble());
                break;
            case JsonValueKind.Null:
                break;
            case JsonValueKind.Array:
                SetArray(dict, key, value);
                break;
            default:
                // Nested objects aren't a great fit for ArchivableDictionary's typed slots —
                // stash the raw JSON so the script can re-parse if it needs to.
                dict.Set(key, value.GetRawText());
                break;
        }
    }

    private static void SetArray(ArchivableDictionary dict, string key, JsonElement array)
    {
        var len = array.GetArrayLength();
        if (len == 0) { dict.Set(key, Array.Empty<string>()); return; }

        var first = array[0].ValueKind;
        if (first == JsonValueKind.Number && AllNumbers(array, out var allInt))
        {
            if (allInt) dict.Set(key, array.EnumerateArray().Select(e => e.GetInt32()).ToArray());
            else dict.Set(key, array.EnumerateArray().Select(e => e.GetDouble()).ToArray());
            return;
        }
        if (first is JsonValueKind.True or JsonValueKind.False && All(array, JsonValueKind.True, JsonValueKind.False))
        {
            dict.Set(key, array.EnumerateArray().Select(e => e.GetBoolean()).ToArray());
            return;
        }

        // Mixed or string array → stringify each element so the script gets something useful.
        dict.Set(key, array.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
            .ToArray());
    }

    private static bool AllNumbers(JsonElement array, out bool allInt)
    {
        allInt = true;
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Number) { allInt = false; return false; }
            if (!e.TryGetInt32(out _)) allInt = false;
        }
        return true;
    }

    private static bool All(JsonElement array, params JsonValueKind[] kinds)
    {
        foreach (var e in array.EnumerateArray())
            if (Array.IndexOf(kinds, e.ValueKind) < 0) return false;
        return true;
    }

    private static Dictionary<string, object?> DictToObject(ArchivableDictionary dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (dict is null) return result;
        foreach (var key in dict.Keys)
            result[key] = dict[key];
        return result;
    }
}
