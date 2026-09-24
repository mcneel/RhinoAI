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
            return string.Format(Localization.LocalizeString("{0} failed", 176), Verb(toolName));

        return toolName switch
        {
            "run_python" => Localization.LocalizeString("ran python", 177),
            "run_csharp" => Localization.LocalizeString("ran C#", 178),
            "run_command" => RunCommand(argsJson),
            "open_doc" => Opened(argsJson),
            "save_doc" => Localization.LocalizeString("saved document", 179),
            "close_doc" => Localization.LocalizeString("closed document", 180),
            "get_selection" => Localization.LocalizeString("read selection", 181),
            "set_selection" => Localization.LocalizeString("set selection", 182),
            "list_objects" => Localization.LocalizeString("listed objects", 183),
            "get_commands" => Localization.LocalizeString("listed commands", 184),
            "get_viewport_image" => Localization.LocalizeString("captured viewport", 185),
            "set_camera" => Localization.LocalizeString("set camera", 186),
            "zoom_to_object" => Localization.LocalizeString("zoomed to object", 187),
            "zoom_to_layer" => Localization.LocalizeString("zoomed to layer", 188),
            "set_layer_material" => Localization.LocalizeString("set layer material", 189),
            "ask_user" => Localization.LocalizeString("asked a question", 190),

            "g1_start" or "g2_start" => Localization.LocalizeString("opened Grasshopper", 191),
            "g1_clear_canvas" or "g2_clear_canvas" => Localization.LocalizeString("cleared the canvas", 192),
            "g1_get_canvas_graph" or "g2_get_canvas_graph" => Localization.LocalizeString("read the canvas", 193),
            "g1_search_components" or "g2_search_components" => Localization.LocalizeString("searched components", 194),
            "g1_describe_component" or "g2_describe_component" => Localization.LocalizeString("described a component", 195),
            "g1_place_component" or "g2_place_component" => Placed(argsJson),
            "g1_place_slider" or "g2_place_slider" => Localization.LocalizeString("placed a slider", 196),
            "g1_connect" or "g2_connect" => Localization.LocalizeString("wired a connection", 197),
            "g1_connect_many" or "g2_connect_many" => Localization.LocalizeString("wired connections", 198),
            "g1_apply_graph" or "g2_apply_graph" => Localization.LocalizeString("applied a graph", 199),
            "g1_solve_graph" or "g2_solve_canvas" => Solved(resultJson),

            null => Localization.LocalizeString("unknown tool", 200),

            _ => toolName.Replace("_", ""),

        };
    }

    private static string RunCommand(string argsJson)
    {
        return TryGetString(argsJson, "command", out string command) && command.Length > 0
            ? string.Format(Localization.LocalizeString("ran {0}", 201), command)
            : Localization.LocalizeString("ran a command", 202);
    }

    private static string Opened(string argsJson)
    {
        if (TryGetString(argsJson, "path", out string path) && path.Length > 0)
            return string.Format(Localization.LocalizeString("opened {0}", 203), FileName(path));
        return Localization.LocalizeString("opened a document", 204);
    }

    private static string Placed(string argsJson)
    {
        if (TryGetString(argsJson, "selector", out string selector) && selector.Length > 0)
            return string.Format(Localization.LocalizeString("placed {0}", 205), selector);
        return Localization.LocalizeString("placed a component", 206);
    }

    // g*_solve returns { solved, errors, warnings, ... }; surface the diagnostic counts when present.
    private static string Solved(string resultJson)
    {
        if (Parse(resultJson) is not { } doc)
            return Localization.LocalizeString("solved the graph", 207);
        using (doc)
        {
            JsonElement root = doc.RootElement;
            int errors = TryGetInt(root, "Errors", out int e) ? e : 0;
            int warnings = TryGetInt(root, "Warnings", out int w) ? w : 0;
            if (errors > 0)
                return string.Format(
                    errors == 1 ? Localization.LocalizeString("solved: {0} error", 208) : Localization.LocalizeString("solved: {0} errors", 209),
                    errors);
            if (warnings > 0)
                return string.Format(
                    warnings == 1 ? Localization.LocalizeString("solved: {0} warning", 210) : Localization.LocalizeString("solved: {0} warnings", 211),
                    warnings);
            return Localization.LocalizeString("solved the graph", 212);
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
            "run_command" => Localization.LocalizeString("command", 213),
            "open_doc" => Localization.LocalizeString("open", 214),
            "save_doc" => Localization.LocalizeString("save", 215),
            "close_doc" => Localization.LocalizeString("close", 216),

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
