using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class PermissionsPage : Panel
{
    private const int RowHeight = 40;
    private const int MaxPlugInRows = 4;
    private const int HeadingGap = 6;

    public PermissionsPage(PermissionsPageViewModel model)
    {
        Model = model;
        DataContext = model;
        Tree = ServerTree();

        TableLayout layout = new() { Spacing = new Size(5, 5) };
        layout.Rows.Add(new TableRow(DefaultPermissionBox()));
        layout.Rows.Add(new TableRow(ServersHeader()));
        layout.Rows.Add(new TableRow(ServersList()) { ScaleHeight = true });
        layout.Rows.Add(new TableRow(new Panel { Padding = new Padding(0, HeadingGap, 0, 0), Content = Heading(LOC.STR("Plug-ins")) }));
        layout.Rows.Add(new TableRow(PlugInsList()));
        Content = layout;
    }

    private PermissionsPageViewModel Model { get; }

    private TreeGridView Tree { get; }

    private static Control DefaultPermissionBox()
    {
        SegmentedPicker<Permissability> picker = PermissionChoices.Picker();
        picker.ValueBinding.BindDataContext((PermissionsPageViewModel m) => m.DefaultPermission);

        Label help = new()
        {
            Text = LOC.STR("Tools you have not set yourself use this."),
            TextColor = Palette.Secondary,
            Wrap = WrapMode.Word,
        };

        StackLayout text = new()
        {
            Spacing = 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { Heading(LOC.STR("Default permission")), help },
        };

        return new TableLayout
        {
            Spacing = new Size(12, 0),
            Rows = { new TableRow(new TableCell(text, true), new TableCell(picker)) },
        };
    }

    private Control ServersHeader()
    {
        Button add = new()
        {
            Text = LOC.STR("Add MCP"),
            ToolTip = LOC.STR("Add an MCP server"),
        };
        add.Click += (_, _) => ShowServerDialog(null);

        SearchBox search = new() { PlaceholderText = LOC.STR("Search tools"), Width = 220 };
        search.TextBinding.BindDataContext((PermissionsPageViewModel m) => m.SearchText);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyData == Keys.Enter)
                e.Handled = true;
        };

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Padding(0, HeadingGap, 0, 0),
            Spacing = 5,
            Items = { new StackLayoutItem(Heading(LOC.STR("MCP servers")), true), add, search },
        };
    }

    private Control ServersList()
    {
        Label empty = new()
        {
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextColor = Palette.Secondary,
        };
        empty.TextBinding.BindDataContext((PermissionsPageViewModel m) => m.EmptyText, DualBindingMode.OneWay);
        Scrollable noMatches = new()
        {
            Border = BorderType.Line,
            ExpandContentWidth = true,
            ExpandContentHeight = true,
            Content = empty,
        };

        Panel host = new() { Content = Tree };
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PermissionsPageViewModel.HasMatches))
                host.Content = Model.HasMatches ? Tree : noMatches;
        };
        return host;
    }

    private TreeGridView ServerTree()
    {
        TreeGridView tree = new()
        {
            ShowHeader = false,
            AllowMultipleSelection = false,
            AllowEmptySelection = true,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            RowHeight = RowHeight,
        };
        tree.Columns.Add(new GridColumn { DataCell = RowCell(), Expand = true, Resizable = false, Sortable = false, CellToolTipBinding = ToolTips() });
        tree.Columns.Add(new GridColumn { DataCell = PermissionCell(), Width = PermissionChoices.PickerWidth + 12, Resizable = false, Sortable = false });
        tree.BindDataContext(view => view.DataStore, (PermissionsPageViewModel m) => m.Tree, DualBindingMode.OneWay);
        tree.Activated += (_, e) =>
        {
            if (e.Item is ServerItemViewModel { Origin: ServerOrigin.Custom } server)
                ShowServerDialog(server);
        };
        tree.ContextMenu = ServerMenu(tree);
        return tree;
    }

    private Control PlugInsList()
    {
        if (Model.PlugIns.Count == 0)
            return new Label { Text = LOC.STR("No plug-ins add tools or skills yet."), TextColor = Palette.Secondary };

        GridView grid = new()
        {
            ShowHeader = false,
            AllowMultipleSelection = false,
            AllowEmptySelection = true,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            RowHeight = RowHeight,
            DataStore = Model.PlugIns,
            Height = Math.Min(Model.PlugIns.Count, MaxPlugInRows) * (RowHeight + 2) + 2,
        };
        grid.Columns.Add(new GridColumn { DataCell = RowCell(), Expand = true, Resizable = false, Sortable = false, CellToolTipBinding = ToolTips() });
        grid.Columns.Add(new GridColumn { DataCell = TrustCell(), Width = 110, Resizable = false, Sortable = false });
        return grid;
    }

    private ContextMenu ServerMenu(TreeGridView tree)
    {
        ITreeGridItem? target = null;
        ButtonMenuItem useDefault = new();
        ButtonMenuItem copyTool = new() { Text = LOC.STR("Copy Tool Name") };
        ButtonMenuItem edit = new() { Text = LOC.STR("Edit…") };
        ButtonMenuItem remove = new() { Text = LOC.STR("Remove") };
        SeparatorMenuItem separator = new();
        ButtonMenuItem copyServer = new() { Text = LOC.STR("Copy Server Name") };

        useDefault.Click += (_, _) =>
        {
            if (target is ToolItemViewModel tool)
                tool.Permission = Permissability.Unset;
        };
        copyTool.Click += (_, _) =>
        {
            if (target is ToolItemViewModel tool)
                Clipboard.Instance.Text = tool.Info.Name;
        };
        edit.Click += (_, _) =>
        {
            if (target is ServerItemViewModel server)
                ShowServerDialog(server);
        };
        remove.Click += (_, _) =>
        {
            if (target is ServerItemViewModel server)
                Model.RemoveServer(server);
        };
        copyServer.Click += (_, _) =>
        {
            if (target is ServerItemViewModel server)
                Clipboard.Instance.Text = server.Name;
        };

        ContextMenu menu = new() { Items = { useDefault, copyTool, edit, remove, separator, copyServer } };
        menu.Opening += (_, _) =>
        {
            target = tree.GetCellAt(tree.PointFromScreen(Mouse.Position))?.Item as ITreeGridItem ?? tree.SelectedItem;
            ToolItemViewModel? tool = target as ToolItemViewModel;
            ServerItemViewModel? server = target as ServerItemViewModel;
            bool isCustom = server?.Origin is ServerOrigin.Custom;

            useDefault.Visible = tool is not null;
            useDefault.Enabled = tool is { Permission: not Permissability.Unset };
            useDefault.Text = tool is null ? string.Empty : string.Format(LOC.STR("Use Default ({0})"), PermissionChoices.Label(tool.Fallback));
            copyTool.Visible = tool is not null;
            edit.Visible = isCustom;
            remove.Visible = isCustom;
            separator.Visible = isCustom;
            copyServer.Visible = server is not null;
        };
        return menu;
    }

    private void ShowServerDialog(ServerItemViewModel? editing)
    {
        McpServerDialogViewModel editor = editing is null ? Model.NewServerEditor() : Model.ServerEditor(editing);
        using McpServerDialog dialog = new(editor);
        if (dialog.ShowModal(this) is not IReadOnlyList<IMcpDefinition> servers)
            return;

        if (Model.SaveServers(editing, servers) is ServerItemViewModel saved)
            Tree.SelectedItem = saved;
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        Font = SystemFonts.Bold(),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static IIndirectBinding<string> ToolTips() => Binding.Delegate<IRowItem, string>(item => item.ToolTip ?? string.Empty);

    private static CustomCell RowCell() => new()
    {
        CreateCell = args =>
        {
            RowTextView view = new();
            view.TextBinding.BindDataContext((IRowItem m) => m.Text, DualBindingMode.OneWay);
            view.TextColorBinding.Bind(Binding.Property(args, (CellEventArgs a) => a.CellTextColor), DualBindingMode.OneWay);
            view.IsSelectedBinding.Bind(Binding.Property(args, (CellEventArgs a) => a.IsSelected), DualBindingMode.OneWay);
            return view;
        },
        ConfigureCell = (args, control) => control.DataContext = args.Item,
    };

    private static CustomCell PermissionCell() => new()
    {
        GetIdentifier = args => args.Item is ToolItemViewModel ? "tool" : "server",
        CreateCell = args =>
        {
            if (args.Item is not ToolItemViewModel)
                return new Panel();

            SegmentedPicker<Permissability> picker = PermissionChoices.Picker();
            picker.ValueBinding.BindDataContext((ToolItemViewModel m) => m.Permission);
            picker.FallbackBinding.BindDataContext((ToolItemViewModel m) => m.Fallback, DualBindingMode.OneWay);
            picker.IsDimmedBinding.BindDataContext((ToolItemViewModel m) => m.IsDimmed, DualBindingMode.OneWay);
            return picker;
        },
        ConfigureCell = (args, control) => control.DataContext = args.Item,
    };

    private static CustomCell TrustCell() => new()
    {
        CreateCell = _ =>
        {
            CheckBox box = new()
            {
                Text = LOC.STR("Enabled"),
                ToolTip = LOC.STR("Let agents use this plug-in's tools and skills."),
            };
            box.CheckedBinding
                .Convert(isChecked => isChecked ?? false, isTrusted => (bool?)isTrusted)
                .BindDataContext((PlugInItemViewModel m) => m.IsTrusted);

            return new TableLayout
            {
                Rows =
                {
                    new TableRow { ScaleHeight = true },
                    new TableRow(box),
                    new TableRow { ScaleHeight = true },
                },
            };
        },
        ConfigureCell = (args, control) => control.DataContext = args.Item,
    };
}
