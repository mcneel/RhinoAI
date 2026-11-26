#if R9
using Rhino.UI;

using InstallResult = Rhino.AI.McpClientConfigInstaller.McpInstallResult;
using InstallState = Rhino.AI.McpClientConfigInstaller.McpInstallState;
using UninstallResult = Rhino.AI.McpClientConfigInstaller.McpUninstallResult;

namespace Rhino.AI.UI.Settings;

internal sealed class ExternalAgentItemViewModel : SimpleViewModel
{
    private InstallState state;
    private AgentProblem? problem;

    public ExternalAgentItemViewModel(McpClientConfigInstaller.McpClient client, InstallState state)
    {
        Client = client;
        this.state = state;
    }

    public McpClientConfigInstaller.McpClient Client { get; }

    public string Name => Client.DisplayName;

    public string ConfigPath => Client.ConfigPath;

    public RowText Text => new(Name, string.Empty, true, Badge, null, state == InstallState.Missing);

    public string ToolTip => state == InstallState.Missing ? string.Format(LOC.STR("Looked for {0}"), Client.AppPath) : ConfigPath;

    public bool HasAction => state != InstallState.Missing;

    public string ActionText => state == InstallState.Installed ? LOC.STR("Remove") : LOC.STR("Add");

    public string ActionToolTip => string.Format(state == InstallState.Installed ? LOC.STR("Remove Rhino from {0}") : LOC.STR("Add Rhino to {0}. Restart {0} to use it."), Name);

    public string ActionMenuText => string.Format(state == InstallState.Installed ? LOC.STR("Remove Rhino from {0}") : LOC.STR("Add Rhino to {0}"), Name);

    public AgentProblem? Problem
    {
        get => problem;
        private set
        {
            if (!SetProperty(ref problem, value))
                return;

            RaisePropertyChanged(nameof(HasProblem));
            RaisePropertyChanged(nameof(ProblemText));
            RaisePropertyChanged(nameof(ProblemAction));
        }
    }

    public bool HasProblem => problem is not null;

    public string ProblemText => problem is AgentProblem current ? Describe(current) : string.Empty;

    public string ProblemAction => problem?.Change switch
    {
        AgentChange.Add => LOC.STR("Set up by hand"),
        AgentChange.Remove => LOC.STR("Copy config path"),
        null => string.Empty,
        _ => throw new InvalidOperationException("Unknown agent change."),
    };

    public void Toggle(IReadOnlyDictionary<string, string> env)
    {
        Problem = state == InstallState.Installed
            ? ProblemOf(AgentChange.Remove, FailureOf(McpClientConfigInstaller.Uninstall(Client)))
            : ProblemOf(AgentChange.Add, FailureOf(McpClientConfigInstaller.Install(Client, env)));
        Refresh(McpClientConfigInstaller.GetState(Client));
    }

    private RowBadge? Badge => state switch
    {
        InstallState.Installed => new RowBadge(LOC.STR("Added"), Palette.Allow),
        InstallState.Missing => new RowBadge(LOC.STR("Not found"), Palette.SeriesOther),
        InstallState.Detected => null,
        _ => throw new InvalidOperationException("Unknown install state."),
    };

    private static AgentProblem? ProblemOf(AgentChange change, AgentFailure? failure)
        => failure is AgentFailure reason ? new AgentProblem(change, reason) : null;

    private static AgentFailure? FailureOf(InstallResult result) => result switch
    {
        InstallResult.Installed or InstallResult.AlreadyInstalled => null,
        InstallResult.Missing => AgentFailure.NotFound,
        InstallResult.Unsupported => AgentFailure.Unsupported,
        InstallResult.Failed => AgentFailure.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    private static AgentFailure? FailureOf(UninstallResult result) => result switch
    {
        UninstallResult.Uninstalled or UninstallResult.NotInstalled => null,
        UninstallResult.Missing => AgentFailure.NotFound,
        UninstallResult.Unsupported => AgentFailure.Unsupported,
        UninstallResult.Failed => AgentFailure.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    private string Describe(AgentProblem current) => (current.Change, current.Failure) switch
    {
        (_, AgentFailure.NotFound) => string.Format(LOC.STR("{0} was not found. Looked for {1}."), Name, Client.AppPath),
        (AgentChange.Add, AgentFailure.Unsupported) => string.Format(LOC.STR("Could not add Rhino to {0}. Its config is in a shape that cannot be edited safely, so add the snippet by hand."), Name),
        (AgentChange.Add, AgentFailure.Failed) => string.Format(LOC.STR("Could not add Rhino to {0}. The command history has the details."), Name),
        (AgentChange.Remove, AgentFailure.Unsupported) => string.Format(LOC.STR("Could not remove Rhino from {0}. Its config is in a shape that cannot be edited safely, so remove the rhino entry by hand."), Name),
        (AgentChange.Remove, AgentFailure.Failed) => string.Format(LOC.STR("Could not remove Rhino from {0}. The command history has the details."), Name),
        _ => throw new ArgumentOutOfRangeException(nameof(current), current, null),
    };

    private void Refresh(InstallState next)
    {
        if (!SetProperty(ref state, next, nameof(Text)))
            return;

        foreach (string property in new[] { nameof(ToolTip), nameof(HasAction), nameof(ActionText), nameof(ActionToolTip) })
            RaisePropertyChanged(property);
    }
}
#endif
