namespace Rhino.AI.Router;

public record RouterConfig(
    string DefaultVersion,
    int StartupTimeoutSeconds = 120,
    IReadOnlyDictionary<string, string>? RhinoExeOverrides = null,
    int ToolCallTimeoutSeconds = 600)
{
    public const int DefaultStartupTimeoutSeconds = 120;

    // Per tool call, router -> Rhino. Matches McpTimeouts.ToolCall (10 min) in
    // rhino/sdk, which the agent harnesses already give their clients. Env var
    // fallback; the `--tool-timeout` CLI arg wins.
    public const int DefaultToolCallTimeoutSeconds = 600;
    public const string ToolCallTimeoutEnvVar = "RHINO_MCP_TOOL_TIMEOUT";

    // Env var fallback for the startup timeout; the `--startup-timeout` CLI arg wins.
    public const string StartupTimeoutEnvVar = "RHINO_MCP_STARTUP_TIMEOUT";

    // Env var fallback for the default version; the `--default-version`/`-v` CLI arg wins.
    public const string DefaultVersionEnvVar = "RHINO_MCP_DEFAULT_VERSION";

    // Per-version path override: point the locator at a custom Rhino (e.g. a
    // from-source debug build) instead of the installed one. Env form is
    // RHINO_MCP_RHINO_EXE_<VERSION> (8/9/WIP); the `--rhino-exe <ver>=<path>`
    // CLI arg wins. Value is a Rhino.exe (Windows) or a .app bundle (macOS).
    public const string RhinoExeEnvPrefix = "RHINO_MCP_RHINO_EXE_";

    private static readonly string[] KnownVersions = ["8", "9", "WIP"];

    public static RouterConfig FromArgs(string[] args)
    {
        string defaultVersion = ReadDefaultVersionFromEnv();
        int startupTimeoutSeconds = ReadStartupTimeoutFromEnv();
        int toolCallTimeoutSeconds = ReadPositiveIntFromEnv(ToolCallTimeoutEnvVar, DefaultToolCallTimeoutSeconds);
        Dictionary<string, string> overrides = ReadRhinoExeOverridesFromEnv();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--default-version" || args[i] == "-v")
            {
                defaultVersion = args[i + 1];
            }
            else if (args[i] == "--startup-timeout")
            {
                if (int.TryParse(args[i + 1], out int parsed) && parsed > 0)
                {
                    startupTimeoutSeconds = parsed;
                }
            }
            else if (args[i] == "--tool-timeout")
            {
                if (int.TryParse(args[i + 1], out int parsed) && parsed > 0)
                {
                    toolCallTimeoutSeconds = parsed;
                }
            }
            else if (args[i] == "--rhino-exe")
            {
                // `<version>=<path>`; the CLI value overrides any env value for that version.
                string raw = args[i + 1];
                int eq = raw.IndexOf('=');
                if (eq > 0)
                {
                    overrides[raw[..eq]] = raw[(eq + 1)..];
                }
            }
        }

        return new RouterConfig(
            defaultVersion,
            startupTimeoutSeconds,
            overrides.Count > 0 ? overrides : null,
            toolCallTimeoutSeconds);
    }

    private static string ReadDefaultVersionFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(DefaultVersionEnvVar);
        return string.IsNullOrWhiteSpace(raw) ? "8" : raw.Trim();
    }

    private static int ReadStartupTimeoutFromEnv() =>
        ReadPositiveIntFromEnv(StartupTimeoutEnvVar, DefaultStartupTimeoutSeconds);

    private static int ReadPositiveIntFromEnv(string name, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out int parsed) && parsed > 0)
        {
            return parsed;
        }
        return fallback;
    }

    private static Dictionary<string, string> ReadRhinoExeOverridesFromEnv()
    {
        Dictionary<string, string> overrides = [];
        foreach (string version in KnownVersions)
        {
            string? path = Environment.GetEnvironmentVariable(RhinoExeEnvPrefix + version);
            if (!string.IsNullOrWhiteSpace(path))
            {
                overrides[version] = path;
            }
        }
        return overrides;
    }
}
