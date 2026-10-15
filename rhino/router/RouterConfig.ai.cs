namespace Rhino.AI.Router;

public record RouterConfig(
    string DefaultVersion,
    int StartupTimeoutSeconds = 120,
    IReadOnlyDictionary<string, string>? RhinoExeOverrides = null,
    SpawnWindowMode WindowMode = SpawnWindowMode.Normal)
{
    public const int DefaultStartupTimeoutSeconds = 120;

    // Env var fallback for the startup timeout; the `--startup-timeout` CLI arg wins.
    public const string StartupTimeoutEnvVar = "RHINO_MCP_STARTUP_TIMEOUT";

    // Env var fallback for the default version; the `--default-version`/`-v` CLI arg wins.
    public const string DefaultVersionEnvVar = "RHINO_MCP_DEFAULT_VERSION";

    // Per-version path override: point the locator at a custom Rhino (e.g. a
    // from-source debug build) instead of the installed one. Env form is
    // RHINO_MCP_RHINO_EXE_<VERSION> (8/9/WIP); the `--rhino-exe <ver>=<path>`
    // CLI arg wins. Value is a Rhino.exe (Windows) or a .app bundle (macOS).
    public const string RhinoExeEnvPrefix = "RHINO_MCP_RHINO_EXE_";

    // Env var fallback for the spawned window's show state; the `--hidden` CLI arg wins.
    // Accepts the same words as the flag, plus the usual boolean spellings.
    public const string WindowModeEnvVar = "RHINO_MCP_HIDDEN";

    private static readonly string[] KnownVersions = ["8", "9", "WIP"];

    public static RouterConfig FromArgs(string[] args)
    {
        string defaultVersion = ReadDefaultVersionFromEnv();
        int startupTimeoutSeconds = ReadStartupTimeoutFromEnv();
        Dictionary<string, string> overrides = ReadRhinoExeOverridesFromEnv();
        SpawnWindowMode windowMode = ReadWindowModeFromEnv();

        // Walk every argument, not every argument but the last: `--hidden` carries no
        // value, so a loop that stops one short would drop it when it is typed last.
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;

            // `--hidden`, `--hidden=minimized`. The attached form keeps the bare flag
            // unambiguous: a following bare word is the next flag, never this one's value.
            if (arg == "--hidden")
            {
                windowMode = SpawnWindowMode.Hidden;
            }
            else if (arg.StartsWith("--hidden=", StringComparison.Ordinal))
            {
                if (TryParseWindowMode(arg["--hidden=".Length..], out SpawnWindowMode parsedMode))
                {
                    windowMode = parsedMode;
                }
            }
            else if (value is null)
            {
                continue;
            }
            else if (arg == "--default-version" || arg == "-v")
            {
                defaultVersion = value;
            }
            else if (arg == "--startup-timeout")
            {
                if (int.TryParse(value, out int parsed) && parsed > 0)
                {
                    startupTimeoutSeconds = parsed;
                }
            }
            else if (arg == "--rhino-exe")
            {
                // `<version>=<path>`; the CLI value overrides any env value for that version.
                int eq = value.IndexOf('=');
                if (eq > 0)
                {
                    overrides[value[..eq]] = value[(eq + 1)..];
                }
            }
        }

        return new RouterConfig(
            defaultVersion,
            startupTimeoutSeconds,
            overrides.Count > 0 ? overrides : null,
            windowMode);
    }

    private static string ReadDefaultVersionFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(DefaultVersionEnvVar);
        return string.IsNullOrWhiteSpace(raw) ? "8" : raw.Trim();
    }

    private static int ReadStartupTimeoutFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(StartupTimeoutEnvVar);
        if (int.TryParse(raw, out int parsed) && parsed > 0)
        {
            return parsed;
        }
        return DefaultStartupTimeoutSeconds;
    }

    private static SpawnWindowMode ReadWindowModeFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(WindowModeEnvVar);
        return TryParseWindowMode(raw, out SpawnWindowMode parsed) ? parsed : SpawnWindowMode.Normal;
    }

    // One spelling table for the flag value and the env var, so `--hidden=minimized`
    // and `RHINO_MCP_HIDDEN=minimized` cannot drift apart.
    private static bool TryParseWindowMode(string? raw, out SpawnWindowMode mode)
    {
        mode = SpawnWindowMode.Normal;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
            case "hidden":
            case "hide":
                mode = SpawnWindowMode.Hidden;
                return true;
            case "min":
            case "minimised":
            case "minimized":
                mode = SpawnWindowMode.Minimized;
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
            case "normal":
                mode = SpawnWindowMode.Normal;
                return true;
            default:
                return false;
        }
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
