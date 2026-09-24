using System.Text.Json;
using Rhino.UI;

namespace Rhino.AI;

/// <summary>
/// Makes verbose tool call names into something simpler
/// </summary>
internal static class ToolSummary
{
    /// <summary>
    /// Tools are named mcp__thing__tool which is verbose, this cleans up
    /// </summary>
    public static string RemoveUnderscoreUnderscoreNaming(string toolName)
    {
        const string prefix = "mcp__";
        if (!toolName.StartsWith(prefix, StringComparison.Ordinal))
            return toolName;

        int separator = toolName.IndexOf("__", prefix.Length, StringComparison.Ordinal);
        return separator < 0 ? toolName : toolName.Substring(separator + 2);
    }

    // result is empty while the call is still in flight; show the in-progress verb without a verdict.
    public static string Describe(string rawToolName, string argsJson, string resultJson, bool reportedFailure = false)
    {
        string toolName = RemoveUnderscoreUnderscoreNaming(rawToolName);
        bool failed = reportedFailure || (!string.IsNullOrWhiteSpace(resultJson) && IsFailure(resultJson));

        string phrase = Phrase(toolName, argsJson, resultJson, failed);
        return phrase;
    }

    // Per-tool phrasing. Returns null to defer to the generic fallback (unknown tool or unreadable
    // payload). A leading failure verb is preferred over a misleading success phrase.
    private static string Phrase(string toolName, string argsJson, string resultJson, bool failed)
    {
        if (failed)
            return string.Format(LOC.STR("{0} failed"), Verb(toolName));

        return toolName switch
        {
            "run_python" => LOC.STR("ran python"),
            "run_csharp" => LOC.STR("ran C#"),
            "run_command" => RunCommand(argsJson),
            "open_doc" => Opened(argsJson),
            "save_doc" => LOC.STR("saved document"),
            "close_doc" => LOC.STR("closed document"),
            "get_selection" => LOC.STR("read selection"),
            "set_selection" => LOC.STR("set selection"),
            "list_objects" => LOC.STR("listed objects"),
            "get_commands" => LOC.STR("listed commands"),
            "get_viewport_image" => LOC.STR("captured viewport"),
            "set_camera" => LOC.STR("set camera"),
            "zoom_to_object" => LOC.STR("zoomed to object"),
            "zoom_to_layer" => LOC.STR("zoomed to layer"),
            "set_layer_material" => LOC.STR("set layer material"),
            "ask_user" => LOC.STR("asked a question"),

            "g1_start" or "g2_start" => LOC.STR("opened Grasshopper"),
            "g1_clear_canvas" or "g2_clear_canvas" => LOC.STR("cleared the canvas"),
            "g1_get_canvas_graph" or "g2_get_canvas_graph" => LOC.STR("read the canvas"),
            "g1_search_components" or "g2_search_components" => LOC.STR("searched components"),
            "g1_describe_component" or "g2_describe_component" => LOC.STR("described a component"),
            "g1_place_component" or "g2_place_component" => Placed(argsJson),
            "g1_place_slider" or "g2_place_slider" => LOC.STR("placed a slider"),
            "g1_connect" or "g2_connect" => LOC.STR("wired a connection"),
            "g1_connect_many" or "g2_connect_many" => LOC.STR("wired connections"),
            "g1_apply_graph" or "g2_apply_graph" => LOC.STR("applied a graph"),
            "g1_solve_graph" or "g2_solve_canvas" => Solved(resultJson),

            null => LOC.STR("unknown tool"),

            _ => toolName.Replace("_", ""),

        };
    }

    private static string RunCommand(string argsJson)
    {
        return TryGetString(argsJson, "command", out string command) && command.Length > 0
            ? string.Format(LOC.STR("ran {0}"), command)
            : LOC.STR("ran a command");
    }

    private static string Opened(string argsJson)
    {
        if (TryGetString(argsJson, "path", out string path) && path.Length > 0)
            return string.Format(LOC.STR("opened {0}"), FileName(path));
        return LOC.STR("opened a document");
    }

    private static string Placed(string argsJson)
    {
        if (TryGetString(argsJson, "selector", out string selector) && selector.Length > 0)
            return string.Format(LOC.STR("placed {0}"), selector);
        return LOC.STR("placed a component");
    }

    // g*_solve returns { solved, errors, warnings, ... }; surface the diagnostic counts when present.
    private static string Solved(string resultJson)
    {
        if (Parse(resultJson) is not { } doc)
            return LOC.STR("solved the graph");
        using (doc)
        {
            JsonElement root = doc.RootElement;
            int errors = TryGetInt(root, "Errors", out int e) ? e : 0;
            int warnings = TryGetInt(root, "Warnings", out int w) ? w : 0;
            if (errors > 0)
                return string.Format(
                    errors == 1 ? LOC.STR("solved: {0} error") : LOC.STR("solved: {0} errors"),
                    errors);
            if (warnings > 0)
                return string.Format(
                    warnings == 1 ? LOC.STR("solved: {0} warning") : LOC.STR("solved: {0} warnings"),
                    warnings);
            return LOC.STR("solved the graph");
        }
    }

    // A short failure verb per tool family so "X failed" reads naturally.
    private static string Verb(string rawToolName)
    {
        string toolName = RemoveUnderscoreUnderscoreNaming(rawToolName);
        return toolName switch
        {
            "run_python" => "python",
            "run_csharp" => "C#",
            "run_command" => LOC.STR("command"),
            "open_doc" => LOC.STR("open"),
            "save_doc" => LOC.STR("save"),
            "close_doc" => LOC.STR("close"),

            _ when toolName.StartsWith("g1_") || toolName.StartsWith("g2_") => "Grasshopper",

            _ => toolName,
        };
    }

    // Common shapes for a failed result: an { Ok: false } flag or a non-empty error/Error string. A
    // malformed or non-object payload counts as success here; the verdict only flips on a clear signal.
    internal static bool IsFailure(string resultJson)
    {
        if (Parse(resultJson) is not { } doc)
            return false;
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("Ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
                return true;
            if (root.TryGetProperty("ok", out JsonElement okLower) && okLower.ValueKind == JsonValueKind.False)
                return true;
            if (HasNonEmptyString(root, "error") || HasNonEmptyString(root, "Error"))
                return true;
            return false;
        }
    }

    private static bool HasNonEmptyString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 };

    private static bool TryGetString(string json, string name, out string value)
    {
        value = string.Empty;
        if (Parse(json) is not { } doc)
            return false;
        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out JsonElement el)
                && el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString() ?? string.Empty;
                return true;
            }
            return false;
        }
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return  root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out JsonElement el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt32(out value);
    }

    // null for absent or malformed JSON; callers own disposal of a returned document.
    private static JsonDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FileName(string path)
    {
        int slash = path.LastIndexOfAny(['/', '\\']);
        return slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
    }
}
