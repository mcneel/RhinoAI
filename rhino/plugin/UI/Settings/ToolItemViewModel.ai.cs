using Eto.Forms;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class ToolItemViewModel : SimpleViewModel, ITreeGridItem, IRowItem
{
    private Permissability permission;

    public ToolItemViewModel(ServerItemViewModel server, ToolInfo info, Permissability saved, bool hasConditions)
    {
        Server = server;
        Parent = server;
        Info = info;
        Saved = saved;
        permission = saved;
        HasConditions = hasConditions;
    }

    public ServerItemViewModel Server { get; }

    public ToolInfo Info { get; }

    private Permissability Saved { get; }

    private bool HasConditions { get; }

    public Permissability Permission
    {
        get => permission;
        set => SetProperty(ref permission, value);
    }

    public Permissability Fallback => Server.Fallback;

    public bool IsDimmed => Server.IsDimmed;

    public bool IsChanged => Permission != Saved;

    public ToolChange Change => new(Server.Name, Info.Name, Permission, Info.Args);

    public bool Expanded { get; set; }

    public bool Expandable => false;

    public ITreeGridItem? Parent { get; set; }

    public RowText Text => new(Info.Title, Detail, false, Badge, null, IsDimmed);

    public string? ToolTip => HasConditions
        ? Summary + "\n\n" + LOC.STR("It also has rules that check its arguments. This page leaves those as they are.")
        : Summary;

    public bool Matches(string query)
        => Phrase.Contains(Info.Title, query) || Phrase.Contains(Info.Name, query) || Phrase.Contains(Info.Description, query);

    public void RefreshFallback() => RaisePropertyChanged(nameof(Fallback));

    public void RefreshDimmed()
    {
        RaisePropertyChanged(nameof(IsDimmed));
        RaisePropertyChanged(nameof(Text));
    }

    private RowBadge? Badge => Info.Effect switch
    {
        ToolEffect.Read => RowBadge.ReadOnly,
        ToolEffect.Edit => null,
        ToolEffect.Destructive => RowBadge.Destructive,
        _ => throw new InvalidOperationException("Unknown tool effect."),
    };

    private string Detail =>Info.Description.Length == 0 ? Info.Name : Info.Name + " · " + Info.Description;

    private string Summary => Info.Description.Length == 0 ? Info.Title : Info.Description;
}
