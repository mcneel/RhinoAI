using System.Collections.ObjectModel;

using Eto.Forms;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class PermissionsPageViewModel : SimpleViewModel
{
    private Permissability defaultPermission;
    private string searchText = string.Empty;
    private TreeGridItemCollection visibleServers;
    private Dictionary<ServerItemViewModel, bool>? expandedBeforeSearch;

    private PermissionsPageViewModel(Permissability savedDefault, List<ServerItemViewModel> servers, IReadOnlyList<PlugInItemViewModel> plugIns)
    {
        SavedDefault = savedDefault;
        defaultPermission = savedDefault;
        Servers = servers;
        PlugIns = new ObservableCollection<PlugInItemViewModel>(plugIns);
        visibleServers = new TreeGridItemCollection(servers);

        foreach (PlugInItemViewModel plugIn in PlugIns)
        {
            plugIn.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PlugInItemViewModel.IsTrusted))
                    RefreshTrust(plugIn);
            };
        }
    }

    private Permissability SavedDefault { get; }

    private List<ServerItemViewModel> Servers { get; }

    private bool CustomServersChanged { get; set; }

    public ObservableCollection<PlugInItemViewModel> PlugIns { get; }

    public Permissability DefaultPermission
    {
        get => defaultPermission;
        set
        {
            if (!SetProperty(ref defaultPermission, value))
                return;

            foreach (ServerItemViewModel server in Servers)
                server.SetPageDefault(value);
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
                Refilter();
        }
    }

    public ITreeGridStore<ITreeGridItem> Tree => visibleServers;

    public bool HasMatches => visibleServers.Count > 0;

    public string EmptyText => string.Format(LOC.STR("No tools match \"{0}\"."), SearchText.Trim());

    public static PermissionsPageViewModel Load()
    {
        UserPreferences preferences = AIInstances.Preferences;
        IReadOnlyList<Permission> rules = [.. preferences.Permissions.All];
        Permissability pageDefault = Shown(preferences.DefaultPermission);
        ToolCatalog catalog = ToolCatalogReader.Current(PermissionRules.Current().Rules, PlugInNames.Current());

        List<ServerItemViewModel> servers =
        [
            new(catalog.Builtin.Name, catalog.Builtin.Title, new ServerOrigin.BuiltIn(), catalog.Builtin.Tools, rules, pageDefault) { Expanded = true },
            new(catalog.DefaultTools.Name, catalog.DefaultTools.Title, new ServerOrigin.BuiltIn(), catalog.DefaultTools.Tools, rules, pageDefault),
        ];

        List<PlugInItemViewModel> plugIns = new(catalog.Plugins.Count);
        foreach (PlugInInfo info in catalog.Plugins)
        {
            Guid id = Guid.Parse(info.Id);
            PlugInItemViewModel plugIn = new(id, info.Name, info.Trusted, info.Loaded, info.Servers.Sum(server => server.Tools.Count), info.Skills, PlugInIcons.Of(id));
            plugIns.Add(plugIn);
            foreach (Rhino.AI.UI.Settings.ServerInfo server in info.Servers)
                servers.Add(new ServerItemViewModel(server.Name, server.Title, new ServerOrigin.PlugIn(plugIn), server.Tools, rules, pageDefault));
        }

        foreach (IMcpDefinition custom in preferences.CustomMcps)
            servers.Add(CustomItem(custom, pageDefault));

        return new PermissionsPageViewModel(pageDefault, servers, plugIns);
    }

    public McpServerDialogViewModel NewServerEditor() => new(NamesExcept(null), null);

    public McpServerDialogViewModel ServerEditor(ServerItemViewModel server) => server.Origin switch
    {
        ServerOrigin.Custom custom => new McpServerDialogViewModel(NamesExcept(server), custom.Definition),
        _ => throw new ArgumentException("Only custom servers can be edited.", nameof(server)),
    };

    public ServerItemViewModel? SaveServers(ServerItemViewModel? replacing, IReadOnlyList<IMcpDefinition> servers)
    {
        int index = replacing is null ? Servers.Count : Servers.IndexOf(replacing);
        if (replacing is not null)
            Servers.RemoveAt(index);

        List<ServerItemViewModel> items = servers.Select(server => CustomItem(server, DefaultPermission)).ToList();
        Servers.InsertRange(index, items);
        CustomServersChanged = true;
        Refilter();
        return items.FirstOrDefault(visibleServers.Contains);
    }

    public void RemoveServer(ServerItemViewModel server)
    {
        if (server.Origin is not ServerOrigin.Custom)
            throw new ArgumentException("Only custom servers can be removed.", nameof(server));

        Servers.Remove(server);
        CustomServersChanged = true;
        Refilter();
    }

    public void Commit()
    {
        UserPreferences preferences = AIInstances.Preferences;
        if (DefaultPermission != SavedDefault)
            preferences.DefaultPermission = DefaultPermission;

        List<ToolChange> changes = Servers
            .SelectMany(server => server.Tools)
            .Where(tool => tool.IsChanged)
            .Select(tool => tool.Change)
            .ToList();
        if (changes.Count > 0)
        {
            IReadOnlyList<Permission> rules = ToolRules.WithChanges([.. preferences.Permissions.All], changes);
            preferences.Permissions.Clear();
            foreach (Permission rule in rules)
                preferences.Permissions.AddPermission(rule);
        }

        List<PlugInPermissionEntry> trust = PlugIns.Where(plugIn => plugIn.IsChanged).Select(plugIn => plugIn.Entry).ToList();
        if (trust.Count > 0)
        {
            Dictionary<Guid, PlugInPermissionEntry> merged = [];
            foreach (PlugInPermissionEntry entry in PlugInPermissionStore.Load().Concat(trust))
                merged[entry.Id] = entry;
            PlugInPermissionStore.Save([.. merged.Values]);
        }

        if (CustomServersChanged)
        {
            preferences.CustomMcps.Clear();
            preferences.CustomMcps.AddRange(Servers.Select(server => server.Origin).OfType<ServerOrigin.Custom>().Select(custom => custom.Definition));
        }
    }

    private void Refilter()
    {
        string query = SearchText.Trim();
        if (query.Length > 0 && expandedBeforeSearch is null)
            expandedBeforeSearch = Servers.ToDictionary(server => server, server => server.Expanded);

        List<ITreeGridItem> matches = [];
        foreach (ServerItemViewModel server in Servers)
        {
            if (!server.Filter(query))
                continue;

            matches.Add(server);
            if (query.Length > 0)
                server.Expanded = server.Count > 0;
        }

        if (query.Length == 0 && expandedBeforeSearch is not null)
        {
            foreach (ServerItemViewModel server in Servers)
                server.Expanded = expandedBeforeSearch.TryGetValue(server, out bool expanded) && expanded;
            expandedBeforeSearch = null;
        }

        visibleServers = new TreeGridItemCollection(matches);
        RaisePropertyChanged(nameof(Tree));
        RaisePropertyChanged(nameof(HasMatches));
        RaisePropertyChanged(nameof(EmptyText));
    }

    private void RefreshTrust(PlugInItemViewModel plugIn)
    {
        foreach (ServerItemViewModel server in Servers)
        {
            if (server.Origin is ServerOrigin.PlugIn owned && owned.Owner == plugIn)
                server.RefreshTrust();
        }
    }

    private IReadOnlyCollection<string> NamesExcept(ServerItemViewModel? except)
        => Servers.Where(server => server != except).Select(server => server.Name).ToList();

    private static ServerItemViewModel CustomItem(IMcpDefinition server, Permissability pageDefault)
        => new(server.Name, server.Name, new ServerOrigin.Custom(server), [], [], pageDefault);

    // Mcp.RunToolAsync only stops Deny and asks for Ask, so an unset default lets every call through.
    private static Permissability Shown(Permissability saved) => saved == Permissability.Unset ? Permissability.Always : saved;
}
