using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>A definition of an AI Agent</summary>
internal sealed record AgentDefinition(string Name, SearchPaths SearchPaths, IReadOnlyList<ModelSpec> Models, string DefaultModel = "default", string DefaultPrompt = "", bool Enabled = true)
{

    public bool Available => SearchPaths.GetPaths().Any();

    public bool? LoggedIn { get; private set; } = null;

    public void EnsureLoggedIn()
    {
        if (LoggedIn ?? false) return;

        // TODO : Log-in
        LoggedIn = true;
    }

    // The profile picks the panel's own model, prompt and MCP route for the spawned CLI.
    public IAgentRunner GetRunner(AIProfile profile, string docTitle) => Name.ToLowerInvariant() switch
    {
        "claude" => new AgentRunner(this, profile, docTitle, (client, convo, cwd) => new StreamJsonAgent(this, client, convo, cwd, new ClaudeStreamJsonParser(this, profile))),
        "codex" => new AgentRunner(this, profile, docTitle, (client, convo, cwd) => new StreamJsonAgent(this, client, convo, cwd, new CodexStreamJsonParser(this, profile, CodexHome.Prepare()))),
        "gemini" => new AgentRunner(this, profile, docTitle, (client, _, cwd) => GeminiConnection.Connect(this, client, cwd)),

        // TODO : Use a better result
        _ => throw new NotImplementedException($"{Name} is not configured")
    };

}

internal sealed record SearchPaths()
{
    [JsonInclude]
    private List<string> Win { get; set; } = [];

    [JsonInclude]
    private List<string> Mac { get; set; } = [];

    private static string USER_PROFILE => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string APPDATA => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LOCAL_APPDATA => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private List<string>? Paths { get; set; }
    public IReadOnlyList<string> GetPaths()
    {
        if (Paths is null)
        {
            List<string> paths = [];
            if (OperatingSystem.IsWindows())
            {
                paths = Win;
            }
            else if (OperatingSystem.IsMacOS())
            {
                paths = Mac;
            }

            // Path Replacements
            for (int i = 0; i < paths.Count; i++)
            {
                paths[i] = paths[i]
                    .Replace("%LOCALAPPDATA%", LOCAL_APPDATA)
                    .Replace("%APPDATA%", APPDATA)
                    .Replace("%USERPROFILE%", USER_PROFILE);
            }

            Paths = paths.SelectMany(p => new Paths.Glob(p, false).TruePaths).ToList();
        }

        return Paths;
    }

}

internal sealed record ModelSpec(string Display, string Id);
