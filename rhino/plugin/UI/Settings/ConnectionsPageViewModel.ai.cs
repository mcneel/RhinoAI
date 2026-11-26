using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class ConnectionsPageViewModel : SimpleViewModel
{
    private bool autoStart;
    private bool autoLoadCommands;
    private McpServerStatus server;
    private HostProblem? serverProblem;
    private bool isServerExpanded = true;
    private bool isAgentsExpanded;
    private bool isSetupExpanded;
    private bool isAdvancedExpanded;

    private ConnectionsPageViewModel(bool autoStart, bool autoLoadCommands, McpServerStatus server, IReadOnlyList<ExternalAgentItemViewModel> agents, IReadOnlyList<RouterOptionItemViewModel> options)
    {
        this.autoStart = autoStart;
        this.autoLoadCommands = autoLoadCommands;
        this.server = server;
        Agents = agents;
        Options = options;
        foreach (RouterOptionItemViewModel option in options)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RouterOptionItemViewModel.Value))
                    RaiseRouterChanged();
            };
        }
    }

    public static ConnectionsPageViewModel Load() => new(
        AIInstances.Preferences.AutoLoadMCP,
        AIInstances.Preferences.AutoLoadScriptPlugIn,
        McpServerHost.Status(),
        McpClientConfigInstaller.Clients.Select(client => new ExternalAgentItemViewModel(client, McpClientConfigInstaller.GetState(client))).ToList(),
        RouterEnvOverride.Catalog.Select(option => new RouterOptionItemViewModel(option, Seed(option))).ToList());

    public bool IsServerRunning => server is McpServerStatus.Running;

    public bool CanToggleServer => server is not McpServerStatus.NoDocument;

    public string ServerStatus => server switch
    {
        McpServerStatus.Running running => string.Format(LOC.STR("Running on port {0} for {1}"), running.Port, running.Document),
        McpServerStatus.Stopped => LOC.STR("Not running"),
        McpServerStatus.NoDocument => LOC.STR("Not running. Open a document to start it."),
        _ => throw new InvalidOperationException("Unknown server status."),
    };

    public string ServerAction => IsServerRunning ? LOC.STR("Stop") : LOC.STR("Start");

    public string ServerActionToolTip => IsServerRunning
        ? LOC.STR("Stop the server. Until it starts again, apps cannot use this Rhino and start a separate one instead.")
        : LOC.STR("Start the server for the active document.");

    public bool HasServerProblem => serverProblem is not null;

    public string ServerProblemText => serverProblem switch
    {
        null => string.Empty,
        HostProblem.NoDocument => LOC.STR("Open a document to start the MCP server."),
        HostProblem.NoFreePort => LOC.STR("No free port is available for the MCP server."),
        HostProblem.StartFailed failed => string.Format(LOC.STR("The MCP server could not start on port {0}."), failed.Port),
        _ => throw new InvalidOperationException("Unknown server problem."),
    };

    public bool AutoStart
    {
        get => autoStart;
        set => SetProperty(ref autoStart, value);
    }

    public bool AutoLoadCommands
    {
        get => autoLoadCommands;
        set => SetProperty(ref autoLoadCommands, value);
    }

    public IReadOnlyList<ExternalAgentItemViewModel> Agents { get; }

    public IReadOnlyList<RouterOptionItemViewModel> Options { get; }

    public string Snippet => RouterMcpConfig.BuildJson(RouterEnvironment());

    public string Prompt => string.Format(
        LOC.STR("Install the RhinoAI MCP server. The entry is:\n\n{0}\n\nIt is very important you tell the user to reload after this."),
        RouterMcpConfig.EntryFragment(RouterEnvironment()));

    public bool IsServerExpanded
    {
        get => isServerExpanded;
        set => SetProperty(ref isServerExpanded, value);
    }

    public bool IsAgentsExpanded
    {
        get => isAgentsExpanded;
        set => SetProperty(ref isAgentsExpanded, value);
    }

    public bool IsSetupExpanded
    {
        get => isSetupExpanded;
        set => SetProperty(ref isSetupExpanded, value);
    }

    public bool IsAdvancedExpanded
    {
        get => isAdvancedExpanded;
        set => SetProperty(ref isAdvancedExpanded, value);
    }

    public void ToggleServer()
    {
        serverProblem = McpServerHost.Toggle();
        server = McpServerHost.Status();
        foreach (string property in new[] { nameof(IsServerRunning), nameof(CanToggleServer), nameof(ServerStatus), nameof(ServerAction), nameof(ServerActionToolTip), nameof(HasServerProblem), nameof(ServerProblemText) })
            RaisePropertyChanged(property);
    }

    public void ToggleAgent(ExternalAgentItemViewModel agent) => agent.Toggle(RouterEnvironment());

    public void Commit()
    {
        UserPreferences preferences = AIInstances.Preferences;
        preferences.AutoLoadMCP = AutoStart;
        preferences.AutoLoadScriptPlugIn = AutoLoadCommands;
    }

    private static string Seed(RouterEnvOverride option)
    {
        if (option == RouterEnvOverride.DefaultVersion && RhinoVersion.Token != option.Default)
            return RhinoVersion.Token;

        if (RunningRhino.SourceBuildOverride is (string variable, string path) && option.EnvVars.Contains(variable))
            return path;

        return string.Empty;
    }

    private IReadOnlyDictionary<string, string> RouterEnvironment()
    {
        Dictionary<string, string> env = new(StringComparer.Ordinal);
        foreach (RouterOptionItemViewModel option in Options.Where(option => option.IsSet))
        {
            foreach (string variable in option.Option.EnvVars)
                env[variable] = option.Value.Trim();
        }

        return env;
    }

    private void RaiseRouterChanged()
    {
        RaisePropertyChanged(nameof(Snippet));
        RaisePropertyChanged(nameof(Prompt));
    }
}
