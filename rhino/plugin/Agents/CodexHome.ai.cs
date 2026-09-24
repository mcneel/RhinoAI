using System.IO;

using Rhino.Runtime;

namespace Rhino.AI;

// A plugin-owned CODEX_HOME, which is what keeps the user's own ~/.codex/config.toml and its MCP servers out of the Rhino agent.
internal static class CodexHome
{
    private const string ConfigResourceName = "Rhino.AI.codex-config.toml";

    private static string? Prepared { get; set; }

    private static string DataStash =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhinoAI", "Codex");

    // A path, not a promise the folder exists: the panel looks inside for renders without provoking the rest of Prepare.
    public static string GeneratedImages => Path.Combine(DataStash, "generated_images");

    public static string Prepare()
    {
        string codexDataStash = DataStash;

        if (Prepared is null)
        {
            Directory.CreateDirectory(codexDataStash);
            File.WriteAllText(Path.Combine(codexDataStash, "config.toml"), ShippedConfig());
            Prepared = codexDataStash;
        }

        CopyAuth(codexDataStash);
        return codexDataStash;
    }

    private static string ShippedConfig()
    {
        using Stream stream = typeof(CodexHome).Assembly.GetManifestResourceStream(ConfigResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ConfigResourceName}' is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void CopyAuth(string home)
    {
        string real = Path.Combine(UserCodexHome(), "auth.json");
        string copy = Path.Combine(home, "auth.json");

        if (!File.Exists(real))
            return;

        try
        {
            File.Delete(copy);
            File.Copy(real, copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HostUtils.LogDebugEvent($"[codex] could not copy auth.json ({ex.Message}); the agent may ask you to sign in again.\n");
        }
    }

    private static string UserCodexHome() =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
}
