using System.Text.Json;

namespace RhMcp;

// Turns a tool call (name + raw input/output JSON) into a short human-readable chip header, e.g.
// "ran python", "placed Circle", "opened model.3dm". Pure and dumb: no Eto, no state, just string in
// / string out. The raw JSON still lives behind the chip's expander, so this never has to be
// lossless. Unknown tools fall back to a generic "<tool>: ok" / "<tool>: failed".
//
// Inputs are best-effort: args/result may be empty (call still streaming) or malformed; every read
// is guarded and degrades to the generic phrase rather than throwing into the render path.
internal static class ToolSummary
{
    // result is empty while the call is still in flight; show the in-progress verb without a verdict.
    public static string Describe(string toolName, string argsJson, string resultJson)
    {
        bool hasResult = !string.IsNullOrWhiteSpace(resultJson);
        bool failed = hasResult && IsFailure(resultJson);

        string? phrase = Phrase(toolName, argsJson, resultJson, failed);
        if (phrase is not null)
            return phrase;

        // Generic fallback keyed only on success/failure: the name carries the rest.
        return hasResult
            ? $"{toolName}: {(failed ? "failed" : "ok")}"
            : toolName;
    }

    // Per-tool phrasing. Returns null to defer to the generic fallback (unknown tool or unreadable
    // payload). A leading failure verb is preferred over a misleading success phrase.
    private static string? Phrase(string toolName, string argsJson, string resultJson, bool failed)
    {
        if (failed)
            return $"{Verb(toolName)} failed";

        return toolName switch
        {
            "run_python" => "ran python",
            "run_csharp" => "ran C#",
            "run_command" => RunCommand(argsJson),
            "open_doc" => Opened(argsJson),
            "save_doc" => "saved document",
            "close_doc" => "closed document",
            "get_selection" => "read selection",
            "set_selection" => "set selection",
            "list_objects" => "listed objects",
            "get_commands" => "listed commands",
            "get_viewport_image" => "captured viewport",
            "set_camera" => "set camera",
            "zoom_to_object" => "zoomed to object",
            "zoom_to_layer" => "zoomed to layer",
            "set_layer_material" => "set layer material",
            "ask_user" => "asked a question",

            "g1_start" or "g2_start" => "opened Grasshopper",
            "g1_clear_canvas" or "g2_clear_canvas" => "cleared the canvas",
            "g1_get_canvas_graph" or "g2_get_canvas_graph" => "read the canvas",
            "g1_search_components" or "g2_search_components" => "searched components",
            "g1_describe_component" or "g2_describe_component" => "described a component",
            "g1_place_component" or "g2_place_component" => Placed(argsJson),
            "g1_place_slider" or "g2_place_slider" => "placed a slider",
            "g1_connect" or "g2_connect" => "wired a connection",
            "g1_connect_many" or "g2_connect_many" => "wired connections",
            "g1_apply_graph" or "g2_apply_graph" => "applied a graph",
            "g1_solve_graph" or "g2_solve_canvas" => Solved(resultJson),

            _ => null,
        };
    }

    private static string RunCommand(string argsJson)
    {
        return TryGetString(argsJson, "command", out string command) && command.Length > 0
            ? $"ran {command}"
            : "ran a command";
    }

    private static string Opened(string argsJson)
    {
        if (TryGetString(argsJson, "path", out string path) && path.Length > 0)
            return $"opened {FileName(path)}";
        return "opened a document";
    }

    private static string Placed(string argsJson)
    {
        if (TryGetString(argsJson, "selector", out string selector) && selector.Length > 0)
            return $"placed {selector}";
        return "placed a component";
    }

    // g*_solve returns { solved, errors, warnings, ... }; surface the diagnostic counts when present.
    private static string Solved(string resultJson)
    {
        if (Parse(resultJson) is not { } doc)
            return "solved the graph";
        using (doc)
        {
            JsonElement root = doc.RootElement;
            int errors = TryGetInt(root, "Errors", out int e) ? e : 0;
            int warnings = TryGetInt(root, "Warnings", out int w) ? w : 0;
            if (errors > 0)
                return $"solved: {errors} error{Plural(errors)}";
            if (warnings > 0)
                return $"solved: {warnings} warning{Plural(warnings)}";
            return "solved the graph";
        }
    }

    // A short failure verb per tool family so "X failed" reads naturally.
    private static string Verb(string toolName) => toolName switch
    {
        "run_python" => "python",
        "run_csharp" => "C#",
        "run_command" => "command",
        "open_doc" => "open",
        "save_doc" => "save",
        "close_doc" => "close",
        _ when toolName.StartsWith("g1_") || toolName.StartsWith("g2_") => "Grasshopper",
        _ => toolName,
    };

    // Common shapes for a failed result: an { Ok: false } flag or a non-empty error/Error string. A
    // malformed or non-object payload counts as success here; the verdict only flips on a clear signal.
    private static bool IsFailure(string resultJson)
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
        return root.TryGetProperty(name, out JsonElement el)
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
        return slash >= 0 && slash + 1 < path.Length ? path[(slash + 1)..] : path;
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
