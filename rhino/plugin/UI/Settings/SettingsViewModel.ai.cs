#if R9
namespace Rhino.AI.UI.Settings;

internal sealed class SettingsViewModel : SimpleViewModel
{
    private const string SettingsKey = "AI";
    private const string PreferencesKey = "Preferences";

    private SettingsViewModel(AgentsPageViewModel agents, PermissionsPageViewModel permissions, ConnectionsPageViewModel connections, UsagePageViewModel usage)
    {
        Agents = agents;
        Permissions = permissions;
        Connections = connections;
        Usage = usage;
    }

    // Rhino owns the Options page, so the AI panels cannot hold a reference to it and listen here instead.
    public static event EventHandler<EventArgs>? Saved;

    public AgentsPageViewModel Agents { get; }

    public PermissionsPageViewModel Permissions { get; }

    public ConnectionsPageViewModel Connections { get; }

    public UsagePageViewModel Usage { get; }

    public static SettingsViewModel Load()
        => new(AgentsPageViewModel.Load(), PermissionsPageViewModel.Load(), ConnectionsPageViewModel.Load(), UsagePageViewModel.Load());

    public bool TryCommit()
    {
        if (!Agents.TryCommit())
            return false;

        Permissions.Commit();
        Connections.Commit();
        SavePreferences();
        Saved?.Invoke(null, EventArgs.Empty);
        return true;
    }

    private static void SavePreferences()
    {
        if (AIInstances.SerializeToJson() is not string json)
            return;

        PersistentSettings settings = PersistentSettings.RhinoAppSettings.AddChild(SettingsKey);
        settings.SetString(PreferencesKey, json);
        settings.HideSettingFromUserInterface(PreferencesKey);
    }
}
#endif
