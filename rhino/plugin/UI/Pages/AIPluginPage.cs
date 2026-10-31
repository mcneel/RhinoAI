using System.Collections.ObjectModel;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Rhino.AI.PlugIns;
using Rhino.UI;

namespace Rhino.AI.UI;

internal sealed class AIPluginPage : AIPage
{

    private AIPluginPageViewModel Model => (DataContext as AIPluginPageViewModel)!;


    public AIPluginPage()
    {
        DataContext = new AIPluginPageViewModel();

        Grid.DataStore = Model.PlugIns;
        Grid.SelectedRow = Model.SelectedIndex;
        Grid.SelectedRowsChanged += (_, _) => Model.SelectedIndex = Grid.SelectedRow;

        Content = new Splitter()
        {
            Panel1 = Grid,
            Panel2 = GetProperties(),
            FixedPanel = SplitterFixedPanel.Panel2,
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Model.Update();
    }

    private static Control GetProperties()
    {
        // layout should be scrollable but elements in it disabled when enabled = false;

        DynamicLayout layout = new();
        layout.BeginVertical();

        // Icon
        ImageView icon = new();
        icon.BindDataContext(l => l.Image, (AIPluginPageViewModel vm) => vm.Selected.Icon);
        layout.Add(icon);

        // Name / id
        Label name = new();
        name.BindDataContext(l => l.Text, (AIPluginPageViewModel vm) => vm.Selected.Name);
        layout.Add(name);

        // Mcps
        GridView mcps = new()
        {
            AllowDrop = false,
            ShowHeader = true,
            Border = BorderType.Line,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            AllowMultipleSelection = false,
            GridLines = GridLines.Horizontal,
            Columns = {
                new GridColumn()
                {
                    HeaderText = "Name",
                    AutoSize = true,
                    Sortable = false,
                    DataCell = new TextBoxCell(nameof(ITool.Name))
                },
                new GridColumn()
                {
                    HeaderText = "ReadOnly",
                    AutoSize = true,
                    Sortable = false,
                    DataCell = new CheckBoxCell(nameof(ITool.ReadOnly))
                },
                new GridColumn()
                {
                    HeaderText = "Destructive",
                    AutoSize = true,
                    Sortable = false,
                    DataCell = new CheckBoxCell(nameof(ITool.Destructive))
                },
            }
        };
        mcps.BindDataContext(m => m.DataStore, (AIPluginPageViewModel vm) => vm.Selected.Tools);
        layout.Add(new Label() { Text = "Tools" });
        layout.Add(mcps);

        // Skills
        GridView skills = new()
        {
            AllowDrop = false,
            ShowHeader = true,
            Border = BorderType.Line,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            AllowMultipleSelection = false,
            GridLines = GridLines.Horizontal,
            Columns = {
                new GridColumn()
                {
                    HeaderText = "Name",
                    AutoSize = true,
                    Sortable = false,
                    DataCell = new TextBoxCell(nameof(ISkill.Name)) { AutoSelectMode = AutoSelectMode.Never }
                }
            }
        };
        skills.BindDataContext(m => m.DataStore, (AIPluginPageViewModel vm) => vm.Selected.Skills);
        layout.Add(new Label() { Text = "Skills" });
        layout.Add(skills);

        layout.EndVertical();
        return layout;
    }

    public GridView Grid { get; } = new()
    {
        AllowDrop = false,
        ShowHeader = true,
        Border = BorderType.Line,
        GridLines = GridLines.None,
        AllowEmptySelection = true,
        AllowColumnReordering = false,
        AllowMultipleSelection = false,
        Columns =
        {
            new GridColumn()
            {
                AutoSize = true,
                Expand = false,
                Sortable = false,
                DataCell = new ImageViewCell(nameof(AgenticPlugInInfo.Icon))
            },
            new GridColumn()
            {
                HeaderText = LOC.STR("Name"),
                AutoSize = true,
                Expand = true,
                Sortable = false,
                DataCell = new TextBoxCell(nameof(AgenticPlugInInfo.Name))
            },
            new GridColumn()
            {
                HeaderText = LOC.STR("Enabled"),
                AutoSize = true,
                Expand = false,
                Sortable = false,
                DataCell = new CheckBoxCell(nameof(AgenticPlugInInfo.Enabled)),
            },
        }
    };

    public override bool Validate()
    {
        if (Model.PlugIns.Count == 0) return true;
        if (Model.PlugIns.Any(p => p.Enabled)) return true;
        // TODO : Claude - Warn user about the fact that NO Agentic stuff can run now, not even the panel
        return false;
    }

    public override bool TryApplyChanges()
    {
        // TODO : Claude - Write back the settings the user chose
        return true;
    }

}

internal sealed class AgenticPlugInInfo
{
    private PlugInToken Token { get; }

    public Guid Id { get; }
    public string Name { get; }
    public Bitmap? Icon { get; }


    public bool Enabled
    {
        get => PlugInRegistry.HasPermission(Token);
        set
        {
            if (value == Enabled) return;
            PlugInRegistry.GivePermission(Token);
        }
    }

    public List<ITool> Tools { get; }
    public List<ISkill> Skills { get; }

    public AgenticPlugInInfo(PlugInToken token, System.Reflection.Assembly assembly, Guid id, string name, string iconResource)
    {
        Token = token;

        Id = id;
        Name = name;
        Icon = GetIcon(assembly, iconResource);

        PlugInRegistry.PrivatePanelMcps.TryGetValue(token, out List<Mcp>? mcps);
        Tools = mcps?.SelectMany(m => m.Tools.Values).ToList() ?? [];

        PlugInRegistry.PrivatePanelSkills.TryGetValue(token, out List<ISkill>? skills);
        Skills = skills ?? [];
    }

    // TODO : Method to get an icon from a .ico --> Check the PlugIn.cs
    private Bitmap? GetIcon(System.Reflection.Assembly assembly, string iconResource)
    {
        if (string.IsNullOrEmpty(iconResource)) return null;
        Stream? stream = assembly.GetManifestResourceStream(iconResource);
        if (stream is null) return null;

        return new System.Drawing.Icon(stream).ToBitmap().ToEto();
    }
}

internal sealed class AIPluginPageViewModel : SimpleViewModel
{

    public ObservableCollection<AgenticPlugInInfo> PlugIns { get; } = new();
    
    private int selectedIndex = 0;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (SetProperty(ref selectedIndex, value))
            {
                RaisePropertyChanged(nameof(Selected));
            }
        }
    }

    // TODO : Default icon Id (Jigsaw puzzle piece?)
    public AgenticPlugInInfo Selected
        => PlugIns.ElementAtOrDefault(SelectedIndex)
            ?? new AgenticPlugInInfo(PlugInToken.Invalid, typeof(Agent).Assembly, Guid.Empty, "N/A", "");

    public AIPluginPageViewModel()
    {
        Update();
    }

    public void Update()
    {
        PlugIns.Clear();
        foreach(KeyValuePair<PlugInToken, PlugInRegistry.AgenticPlugIn> kvp in PlugInRegistry.PlugIns)
        {
            PlugIns.Add(new AgenticPlugInInfo(kvp.Key, kvp.Value.Assembly, kvp.Value.Id, kvp.Value.Name, kvp.Value.IconResource));
        }

        RaisePropertyChanged(nameof(Selected));
    }

}
