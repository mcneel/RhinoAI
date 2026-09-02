namespace RhinoAI;

// One router env override the Connect dialog's Advanced tab exposes. The router
// runs as a separate AOT binary, so its env-var names can't be shared as
// constants: the EnvVars strings here MUST match what RhinoAI.Router.RouterConfig
// and RouterPaths read.
internal sealed record RouterEnvOverride(IReadOnlyList<string> EnvVars, string Label, string Default, string Help)
{
    // A trimmed value that differs from the default is a real override worth
    // emitting into the env block; anything else means "leave it at the default".
    public bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim() != Default;

    // Placeholder text shown in the empty field: the default value, or a hint
    // when the default is "unset".
    public string Placeholder => Default.Length > 0 ? Default : "(installed default)";

    public static RouterEnvOverride DefaultVersion { get; } =
        new(["RHINO_MCP_DEFAULT_VERSION"], "Default version", "8",
            "Rhino version auto-spawned for un-pinned tool calls: 8, 9, or WIP.");

    public static IReadOnlyList<RouterEnvOverride> Catalog { get; } =
    [
        DefaultVersion,
        new(["RHINO_MCP_STARTUP_TIMEOUT"], "Startup timeout (s)", "120",
            "Seconds to wait for Rhino to bind its port before failing. Raise for slow or debug builds."),
        new(["RHINO_MCP_RHINO_EXE_8"], "Rhino 8 path", "",
            "Custom Rhino.exe (Windows) or .app bundle (macOS) to launch for version 8."),
        new(["RHINO_MCP_RHINO_EXE_9", "RHINO_MCP_RHINO_EXE_WIP"], "Rhino 9/WIP path", "",
            "Custom Rhino.exe / .app to launch for version 9 and WIP, which resolve to one build."),
        // RHINO_MCP_HOME is deliberately NOT exposed: it's a shared contract read
        // independently by the router and the plugin (RouterPaths is linked into
        // both). Setting it in this env block reaches only the router, so the
        // listener-announcement dir diverges from where the plugin writes and
        // adoption silently breaks. It must be set globally for both or not at all.
    ];
}
