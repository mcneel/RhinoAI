using System.Collections.Specialized;

using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class AgentsPage : Panel
{
    private const int ListWidth = 250;
    private const int RowHeight = 44;
    private const int StarWidth = 30;
    private const int Gap = 12;

    private bool isSyncing;
    private AgentItemViewModel? fresh;

    public AgentsPage(AgentsPageViewModel model)
    {
        Model = model;
        DataContext = model;
        List = AgentList();
        Detail = new Panel();
        Adding = AddMenu();

        Button add = new() { Text = LOC.STR("Add Agent…") };
        add.Click += (_, _) => Adding.Show(add, new PointF(0, add.Height));
        Button reset = new() { Text = LOC.STR("Reset") };
        reset.Click += (_, _) => ConfirmReset();

        TableLayout left = new()
        {
            Width = ListWidth,
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(List) { ScaleHeight = true },
                new TableRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(add, reset, null) } }),
            },
        };

        Content = new TableLayout
        {
            Rows =
            {
                new TableRow(Banner(Note(LOC.STR("Internet access is off in Rhino. Agents cannot connect until you turn it on again in Rhino Options."), Palette.Warning), model.IsOffline)),
                new TableRow(PageProblem()),
                new TableRow(new TableLayout
                {
                    Spacing = new Size(Gap, 0),
                    Rows = { new TableRow(new TableCell(left), new TableCell(Detail, true)) },
                }) { ScaleHeight = true },
            },
        };

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentsPageViewModel.Selected))
                ShowSelected();
        };
        model.Agents.CollectionChanged += (_, e) => Track(e);
        List.SelectionChanged += (_, _) =>
        {
            if (!isSyncing && List.SelectedItem is AgentItemViewModel agent)
                model.Selected = agent;
        };
        List.LoadComplete += (_, _) => ShowSelected();
        ShowSelected();
    }

    private AgentsPageViewModel Model { get; }

    private GridView List { get; }

    private Panel Detail { get; }

    private ContextMenu Adding { get; }

    private Dictionary<AgentItemViewModel, AgentDetailView> Details { get; } = [];

    private void ShowSelected()
    {
        AgentItemViewModel? agent = Model.Selected;
        int row = agent is null ? -1 : Model.Agents.IndexOf(agent);
        if (List.SelectedRow != row)
        {
            isSyncing = true;
            List.SelectedRow = row;
            isSyncing = false;
        }

        if (row >= 0)
            List.ScrollToRow(row);

        if (agent is null)
        {
            Detail.Content = new Label
            {
                Text = Model.IsEmpty ? LOC.STR("No agents yet. Add an agent to start working with AI in Rhino.") : string.Empty,
                TextColor = Palette.Secondary,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        if (!Details.TryGetValue(agent, out AgentDetailView? view))
            Details[agent] = view = new AgentDetailView(Model, agent);
        Detail.Content = view;

        if (ReferenceEquals(agent, fresh))
        {
            fresh = null;
            Application.Instance.AsyncInvoke(view.FocusName);
        }
    }

    private void ConfirmReset()
    {
        DialogResult answer = Eto.Forms.MessageBox.Show(
            this,
            LOC.STR("This removes every agent, including any you added, and restores the built-in agents with their original settings. Saved API keys are kept. This cannot be undone."),
            LOC.STR("Reset Agents?"),
            MessageBoxButtons.YesNo,
            MessageBoxType.Warning,
            MessageBoxDefaultButton.No);

        if (answer == DialogResult.Yes)
            Model.Reset();
    }

    private void Track(NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Reset)
            Details.Clear();

        if (change.OldItems is not null)
        {
            foreach (AgentItemViewModel gone in change.OldItems.OfType<AgentItemViewModel>())
                Details.Remove(gone);
        }

        if (change.NewItems?.OfType<AgentItemViewModel>().LastOrDefault() is AgentItemViewModel { IsNew: true } added)
            fresh = added;
    }

    private GridView AgentList()
    {
        GridView grid = new()
        {
            ShowHeader = false,
            AllowMultipleSelection = false,
            AllowEmptySelection = true,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            RowHeight = RowHeight,
            DataStore = Model.Agents,
        };
        grid.Columns.Add(new GridColumn
        {
            DataCell = AgentCell(),
            Expand = true,
            Resizable = false,
            Sortable = false,
            CellToolTipBinding = Binding.Property((AgentItemViewModel m) => m.ToolTip),
        });
        grid.Columns.Add(new GridColumn { DataCell = StarCell(), Width = StarWidth, Resizable = false, Sortable = false });
        grid.ContextMenu = AgentMenu(grid);
        return grid;
    }

    private static CustomCell AgentCell() => new()
    {
        CreateCell = args =>
        {
            RowTextView view = new();
            view.TextBinding.BindDataContext((AgentItemViewModel m) => m.Text, DualBindingMode.OneWay);
            view.TextColorBinding.Bind(Binding.Property(args, (CellEventArgs a) => a.CellTextColor), DualBindingMode.OneWay);
            view.IsSelectedBinding.Bind(Binding.Property(args, (CellEventArgs a) => a.IsSelected), DualBindingMode.OneWay);
            return view;
        },
        ConfigureCell = (args, control) => control.DataContext = args.Item,
    };

    private CustomCell StarCell() => new()
    {
        CreateCell = _ =>
        {
            DefaultStar star = new();
            star.IsDefaultBinding.BindDataContext((AgentItemViewModel m) => m.IsDefault, DualBindingMode.OneWay);
            star.IsAvailableBinding.BindDataContext((AgentItemViewModel m) => m.Enabled, DualBindingMode.OneWay);
            star.BindDataContext(control => control.ToolTip, (AgentItemViewModel m) => m.StarToolTip, DualBindingMode.OneWay);
            star.Pressed += (_, _) =>
            {
                if (star.DataContext is AgentItemViewModel agent)
                    Model.MakeDefault(agent);
            };
            return star;
        },
        ConfigureCell = (args, control) => control.DataContext = args.Item,
    };

    private ContextMenu AgentMenu(GridView grid)
    {
        AgentItemViewModel? target = null;
        CheckMenuItem isDefault = new() { Text = LOC.STR("Default Agent") };
        CheckMenuItem enabled = new() { Text = LOC.STR("Enabled") };
        ButtonMenuItem duplicate = new() { Text = LOC.STR("Duplicate") };
        ButtonMenuItem remove = new() { Text = LOC.STR("Remove") };

        isDefault.Click += (_, _) =>
        {
            if (target is not null)
                Model.MakeDefault(target);
        };
        enabled.Click += (_, _) =>
        {
            if (target is not null)
                target.Enabled = !target.Enabled;
        };
        duplicate.Click += (_, _) =>
        {
            if (target is not null)
                Model.Duplicate(target);
        };
        remove.Click += (_, _) =>
        {
            if (target is not null)
                AgentRemoval.Confirm(grid, Model, target);
        };

        ContextMenu menu = new() { Items = { isDefault, enabled, new SeparatorMenuItem(), duplicate, remove } };
        menu.Opening += (_, _) =>
        {
            target = grid.GetCellAt(grid.PointFromScreen(Mouse.Position))?.Item as AgentItemViewModel ?? grid.SelectedItem as AgentItemViewModel;
            foreach (MenuItem item in new MenuItem[] { isDefault, enabled, duplicate, remove })
                item.Enabled = target is not null;
            isDefault.Enabled = target is { Enabled: true };
            isDefault.Checked = target?.IsDefault == true;
            enabled.Checked = target?.Enabled == true;
        };
        return menu;
    }

    private ContextMenu AddMenu()
    {
        ContextMenu menu = new();
        foreach (AgentTemplateGroup group in Model.TemplateGroups)
        {
            ButtonMenuItem submenu = new() { Text = group.Label };
            foreach (AgentSettings template in group.Templates)
            {
                ButtonMenuItem item = new() { Text = template.Name };
                item.Click += (_, _) => Model.AddFromTemplate(template);
                submenu.Items.Add(item);
            }

            menu.Items.Add(submenu);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new SeparatorMenuItem());

        ButtonMenuItem api = new() { Text = LOC.STR("Custom API Endpoint") };
        api.Click += (_, _) => Model.AddCustom(AgentKind.Api);
        ButtonMenuItem local = new() { Text = LOC.STR("Custom Local Server") };
        local.Click += (_, _) => Model.AddCustom(AgentKind.Local);
        menu.Items.Add(api);
        menu.Items.Add(local);
        return menu;
    }

    private static Control PageProblem()
    {
        Label problem = Note(string.Empty, Palette.Danger);
        problem.TextBinding.BindDataContext((AgentsPageViewModel m) => m.PageProblem, DualBindingMode.OneWay);
        Panel banner = new() { Padding = new Padding(0, 0, 0, 8), Content = problem };
        banner.BindDataContext(control => control.Visible, (AgentsPageViewModel m) => m.HasPageProblem, DualBindingMode.OneWay);
        return banner;
    }

    private static Control Banner(Control content, bool isVisible) => new Panel
    {
        Padding = new Padding(0, 0, 0, 8),
        Content = content,
        Visible = isVisible,
    };

    private static Label Note(string text, Color colour) => new() { Text = text, TextColor = colour, Wrap = WrapMode.Word };
}
