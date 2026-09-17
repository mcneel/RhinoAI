namespace Rhino.AI.Server;

// How the in-Rhino agent may use a tool. Ask puts a native Yes/No in front of every call.
internal enum ToolMode { Off, On, Ask }

// The settings and wire spelling of a mode ("off" | "on" | "ask") and of a per-tool override ("name=mode").
internal static class ToolModes
{
    public static ToolMode DefaultFor(bool enabledByDefault, bool confirmByDefault) =>
        !enabledByDefault ? ToolMode.Off : confirmByDefault ? ToolMode.Ask : ToolMode.On;

    public static string Format(ToolMode mode) => mode switch
    {
        ToolMode.Off => "off",
        ToolMode.Ask => "ask",
        _ => "on",
    };

    public static bool TryParse(string? text, out ToolMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "off": mode = ToolMode.Off; return true;
            case "on": mode = ToolMode.On; return true;
            case "ask": mode = ToolMode.Ask; return true;
            default: mode = ToolMode.On; return false;
        }
    }

    public static string FormatEntry(string name, ToolMode mode) => $"{name}={Format(mode)}";

    public static bool TryParseEntry(string? entry, out string name, out ToolMode mode)
    {
        name = string.Empty;
        mode = ToolMode.On;
        if (entry is null)
            return false;
        int separator = entry.IndexOf('=');
        if (separator <= 0)
            return false;
        name = entry[..separator].Trim();
        return name.Length > 0 && TryParse(entry[(separator + 1)..], out mode);
    }

    public static bool IsFor(string entry, string name) =>
        TryParseEntry(entry, out string entryName, out _)
        && string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase);
}
