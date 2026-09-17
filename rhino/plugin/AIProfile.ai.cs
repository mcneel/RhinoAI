namespace Rhino.AI;

// The chat surfaces. Each is its own assistant: its own conversation and history, its own prompt
// steer and its own tool permissions. Which agent runs, on which model, with whose prompt is NOT
// per assistant — that is one set of settings for all three. Rhino keeps the settings keys that
// predate profiles, so an upgrade changes nothing for it.
internal enum AIProfile { Rhino, Script, Grasshopper }

internal static class AIProfiles
{
    public static IReadOnlyList<AIProfile> All { get; } = [AIProfile.Rhino, AIProfile.Script, AIProfile.Grasshopper];

    // The command name, the panel caption, and what every dialog calls this assistant.
    public static string Name(AIProfile profile) => profile switch
    {
        AIProfile.Script => "ScriptAssistant",
        AIProfile.Grasshopper => "GrasshopperAssistant",
        _ => "RhinoAssistant",
    };

    // Prefix on the per-assistant settings keys, which is now the tool permissions alone. Rhino
    // carries none: its keys predate profiles and renaming them would silently reset a user's
    // permissions.
    public static string SettingsPrefix(AIProfile profile) =>
        profile == AIProfile.Rhino ? string.Empty : Name(profile) + "_";

    // The in-Rhino MCP route this profile's agent is pointed at, so the server can apply that panel's
    // tool permissions. The external route is "/" and belongs to no profile.
    public static string Route(AIProfile profile) => "/" + Wire(profile);

    // Wire and transcript spelling.
    public static string Wire(AIProfile profile) => profile switch
    {
        AIProfile.Script => "script",
        AIProfile.Grasshopper => "grasshopper",
        _ => "rhino",
    };

    // Unknown text reads as Rhino, which is what a transcript saved before profiles existed was.
    // "ai" and "assistant" are the spellings used before the panels were renamed.
    public static AIProfile Parse(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "script" or "assistant" => AIProfile.Script,
        "grasshopper" => AIProfile.Grasshopper,
        _ => AIProfile.Rhino,
    };
}
