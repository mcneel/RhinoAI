using Eto.Forms;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class ServerItemViewModel : SimpleViewModel, ITreeGridItem<ITreeGridItem>, IRowItem
{
    private List<ToolItemViewModel> visible;
    private Permissability pageDefault;

    public ServerItemViewModel(string name, string title, ServerOrigin origin, IReadOnlyList<ToolInfo> tools, IReadOnlyList<Permission> rules, Permissability pageDefault)
    {
        Name = name;
        Title = title;
        Origin = origin;
        this.pageDefault = pageDefault;
        ServerWide = ToolRules.ServerWide(rules, name);
        Tools = tools
            .Select(tool => new ToolItemViewModel(this, tool, ToolRules.Explicit(rules, name, tool.Name), ToolRules.HasConditions(rules, name, tool.Name)))
            .ToList();
        visible = [.. Tools];
    }

    public string Name { get; }

    public string Title { get; }

    public ServerOrigin Origin { get; }

    public IReadOnlyList<ToolItemViewModel> Tools { get; }

    private Permissability ServerWide { get; }

    public Permissability Fallback => ServerWide == Permissability.Unset ? pageDefault : ServerWide;

    public bool IsDimmed => Origin is ServerOrigin.PlugIn { Owner.IsTrusted: false };

    public int Count => visible.Count;

    public ITreeGridItem this[int index] => visible[index];

    public bool Expanded { get; set; }

    public bool Expandable => visible.Count > 0;

    public ITreeGridItem? Parent { get; set; }

    public RowText Text => new(Title, Detail, true, null, null, IsDimmed);

    public string? ToolTip
    {
        get
        {
            string? origin = Origin switch
            {
                ServerOrigin.BuiltIn => null,
                ServerOrigin.PlugIn { Owner.IsTrusted: false } plugIn => string.Format(LOC.STR("Enable {0} under Plug-ins to let agents use these tools."), plugIn.Owner.Name),
                ServerOrigin.PlugIn => null,
                ServerOrigin.Custom custom => Launch(custom.Definition),
                _ => throw new InvalidOperationException("Unknown server origin."),
            };
            string? serverWide = ServerWide == Permissability.Unset
                ? null
                : string.Format(LOC.STR("Tools you have not set use {0}, from a rule for the whole server."), PermissionChoices.Label(ServerWide));

            string[] lines = new[] { origin, serverWide }.OfType<string>().ToArray();
            return lines.Length == 0 ? null : string.Join("\n\n", lines);
        }
    }

    private string Detail => Origin switch
    {
        ServerOrigin.BuiltIn => Phrase.Count(Tools.Count, LOC.STR("Built into Rhino, 1 tool"), LOC.STR("Built into Rhino, {0} tools")),
        ServerOrigin.PlugIn { Owner.IsTrusted: false } plugIn => string.Format(LOC.STR("From {0}. Not enabled, so agents cannot use these tools."), plugIn.Owner.Name),
        ServerOrigin.PlugIn plugIn => string.Format(Tools.Count == 1 ? LOC.STR("From {0}, 1 tool") : LOC.STR("From {0}, {1} tools"), plugIn.Owner.Name, Tools.Count),
        ServerOrigin.Custom custom => string.Format(LOC.STR("Custom · {0}"), Launch(custom.Definition)),
        _ => throw new InvalidOperationException("Unknown server origin."),
    };

    public void SetPageDefault(Permissability value)
    {
        if (pageDefault == value)
            return;

        pageDefault = value;
        foreach (ToolItemViewModel tool in Tools)
            tool.RefreshFallback();
    }

    public void RefreshTrust()
    {
        RaisePropertyChanged(nameof(IsDimmed));
        RaisePropertyChanged(nameof(Text));
        foreach (ToolItemViewModel tool in Tools)
            tool.RefreshDimmed();
    }

    public bool Filter(string query)
    {
        bool nameMatches = query.Length == 0 || Phrase.Contains(Title, query) || Phrase.Contains(Name, query);
        visible = nameMatches ? [.. Tools] : Tools.Where(tool => tool.Matches(query)).ToList();
        return nameMatches || visible.Count > 0;
    }

    private static string Launch(IMcpDefinition definition) => definition switch
    {
        HttpMcpDefinition http => http.Url.OriginalString,
        StdioMcpDefinition stdio => string.Join(" ", new[] { stdio.Command }.Concat(stdio.Args)),
        _ => throw new InvalidOperationException("Unknown MCP definition."),
    };
}
