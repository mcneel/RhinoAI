using System.Collections.ObjectModel;
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI;

// One assistant's settings: the pages that edit them, and the commit that writes them back. Hosted
// by AISettingsTabs, which the standalone AISettingsDialog and the Rhino Options page both show;
// neither owns the commit logic, it lives here behind TryCommit so the hosts can never drift.
internal sealed class AISettingsPanel
{
    private ObservableCollection<AgentRow> Rows { get; } = [];
    private GridView AgentGrid { get; } = new() { ShowHeader = true, AllowMultipleSelection = false, AllowColumnReordering = false, AllowEmptySelection = true };

    // Right-hand property list for the selected agent. Model/Enabled/Available used to live in the grid;
    // they moved here so the grid stays a plain selection list and every editable property reads top-down.
    private Label NameHeader { get; } = new() { Font = Fonts.Sans(13, FontStyle.Bold) };
    private Label AvailableLabel { get; } = new();
    private CheckBox EnabledBox { get; } = new() { Text = Rhino.UI.LOC.STR("Enabled") };
    private DropDown ModelBox { get; } = new();
    private TextArea SearchPathsBox { get; } = new() { Wrap = false, Height = 70, ReadOnly = true };
    private TextArea SystemPromptBox { get; } = new() { Wrap = true, Height = 90 };

    private TextArea McpJsonBox { get; } = new() { Wrap = false, Font = Fonts.Monospace(11) };
    private Label McpErrorLabel { get; } = new() { TextColor = Colors.Red, Visible = false };

    // Leaf tool rows in the Tools tree, kept flat so Commit can read each checkbox back
    // without re-walking the grouped tree.
    private List<ToolNode> ToolLeaves { get; } = [];

    private static JsonSerializerOptions IndentedJson { get; } = new() { WriteIndented = true };
    private const string EmptyMcpJson = "{\n  \"mcpServers\": {}\n}";

    // Sentinel shown in the grid's Model dropdown for an empty model. Picking it stores an empty
    // string, i.e. "pass no --model, let the CLI choose its own default".
    private static string DefaultModelLabel => Rhino.UI.LOC.STR("(default)");

    // Suppresses the editor->row write-back while we are programmatically loading
    // the editor from a freshly selected row.
    private bool Loading { get; set; }

    // Whose settings this page edits. Extra MCP servers are shared by every agent, so only the AI
    // profile's page shows that tab.
    public AIProfile Profile { get; }

    // Not a control in its own right any more: it reads one assistant's settings, hands out the pages
    // that edit them and writes them back. AISettingsTabs decides where those pages go, which is how
    // one assistant's permissions can sit on the same row of tabs as another's.
    public AISettingsPanel(AIProfile profile)
    {
        Profile = profile;
        SeedRows();
    }

    /// <summary>The agents this assistant may use, and the model and prompt of the selected one.</summary>
    public Control Agents() => Pad(AgentsTab());

    /// <summary>The extra MCP servers every agent sees. Shared, so only the Rhino page offers it.</summary>
    public Control Mcp() => Pad(McpServersTab());

    /// <summary>What this assistant's agent is allowed to do.</summary>
    public Control Permissions() => Pad(ToolsTab());

    private static Control Pad(Control page) => new Eto.Forms.Panel { Padding = new Padding(12), Content = page };

    // Persists every tab back to AISettings. Returns false (and shows the MCP error inline) when the
    // MCP JSON is invalid, so the hosting dialog/page can keep itself open; true once everything is saved.
    public bool TryCommit(out string error)
    {
        error = string.Empty;

        string normalizedJson = string.Empty;
        if (Profile == AIProfile.Rhino)
        {
            if (!TryValidateMcpJson(McpJsonBox.Text, out normalizedJson, out string validationError))
            {
                McpErrorLabel.Text = validationError;
                McpErrorLabel.Visible = true;
                error = validationError;
                return false;
            }
            McpErrorLabel.Visible = false;
        }

        foreach (AgentRow row in Rows)
        {
            AISettings.SetAgentEnabled(row.Name, row.Enabled);
            AISettings.SetAgentModel(row.Name, row.Model);
            AISettings.SetAgentPrompt(row.Name, row.SystemPrompt);
        }

        if (Rows.FirstOrDefault(r => r.IsDefault) is AgentRow defaultRow)
            AISettings.SetDefaultAgentName(defaultRow.Name);

        if (Profile == AIProfile.Rhino)
            AISettings.ExtraMcpServersJson = normalizedJson;

        foreach (ToolNode leaf in ToolLeaves)
            if (leaf.Tool is { } tool)
                ToolPolicy.SetMode(Profile, tool, leaf.Mode);

        return true;
    }

    private void SeedRows()
    {
        string defaultName = AISettings.DefaultAgentName();
        bool anyDefault = false;
        foreach (AgentDefinition def in AgentRegistry.Instance.AllDefinitions)
        {
            bool isDefault = string.Equals(def.Name, defaultName, StringComparison.OrdinalIgnoreCase);
            anyDefault |= isDefault;
            Rows.Add(AgentRow.From(def, isDefault));
        }

        if (!anyDefault && Rows.Count > 0)
            Rows[0].IsDefault = true;
    }

    private Control AgentsTab()
    {
        AgentGrid.DataStore = Rows;
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = Rhino.UI.LOC.STR("Default"),
            HeaderTextAlignment = TextAlignment.Center,
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.DefaultGlyph), TextAlignment = TextAlignment.Center },
            Editable = false,
            Resizable = false,
            AutoSize = true,
        });
        AgentGrid.Columns.Add(new GridColumn
        {
            HeaderText = Rhino.UI.LOC.STR("Agent"),
            HeaderTextAlignment = TextAlignment.Center,
            DataCell = new TextBoxCell { Binding = Binding.Property((AgentRow r) => r.Name), TextAlignment = TextAlignment.Center },
            Editable = false,
            Resizable = false,
            Width = 140,
        });

        AgentGrid.SelectionChanged += (_, _) => LoadEditor();
        AgentGrid.ContextMenu = BuildGridContextMenu();

        EnabledBox.CheckedChanged += (_, _) => WriteEditor(row => row.Enabled = EnabledBox.Checked == true);
        ModelBox.SelectedValueChanged += (_, _) => WriteEditor(row => row.Model = CurrentModelValue());
        SystemPromptBox.TextChanged += (_, _) => WriteEditor(row => row.SystemPrompt = SystemPromptBox.Text);

        StackLayout left = new()
        {
            Width = 200,
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(AgentGrid, expand: true),
            },
        };

        TableLayout properties = new()
        {
            Spacing = new Size(8, 6),
            Rows =
            {
                new TableRow(NameHeader),
                new TableRow(AvailableLabel),
                new TableRow(EnabledBox),
                new TableRow(LabeledColumn(Rhino.UI.LOC.STR("Model:"), ModelBox)),
                new TableRow(LabeledColumn(Rhino.UI.LOC.STR("Found at:"), SearchPathsBox)),
                new TableRow(LabeledColumn(Rhino.UI.LOC.STR("Prompt:"), SystemPromptBox)),
                new TableRow { ScaleHeight = true },
            },
        };

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = Rhino.UI.LOC.STR("Which agent runs, on which model, with which prompt — one set of settings, "
                + "shared by every assistant. Only what each of them is allowed to do is its own, on the "
                + "Permissions tab."),
            TextColor = Colors.Gray,
        };

        TableLayout layout = new()
        {
            Padding = new Padding(8),
            Spacing = new Size(12, 8),
            Rows =
            {
                new TableRow(new TableCell(help), new TableCell(new Panel())),
                new TableRow(
                    new TableCell(left),
                    new TableCell(properties, scaleWidth: true))
                { ScaleHeight = true },
            },
        };

        LoadEditor();
        return layout;
    }

    // Label stacked above its control, so a multi-line box gets full width instead of a left-hand label
    // eating into it.
    private static Control LabeledColumn(string label, Control control) =>
        new TableLayout
        {
            Spacing = new Size(0, 3),
            Rows = { new TableRow(new Label { Text = label }), new TableRow(new TableCell(control, true)) },
        };

    private ContextMenu BuildGridContextMenu()
    {
        ButtonMenuItem setDefault = new() { Text = Rhino.UI.LOC.STR("Set Default") };
        setDefault.Click += (_, _) => SetSelectedDefault();
        ButtonMenuItem reset = new() { Text = Rhino.UI.LOC.STR("Restore Defaults") };
        reset.Click += (_, _) => ResetSelected();

        ContextMenu menu = new() { Items = { setDefault, reset } };
        menu.Opening += (_, _) =>
        {
            bool hasSelection = TryGetSelected(out AgentRow _);
            setDefault.Enabled = hasSelection;
            reset.Enabled = hasSelection;
        };
        return menu;
    }

    private void ResetSelected()
    {
        if (!TryGetSelected(out AgentRow row))
            return;

        DialogResult confirm = MessageBox.Show(
            Rhino.UI.RhinoEtoApp.MainWindow,
            string.Format(
                Rhino.UI.LOC.STR("Reset \"{0}\" to its default settings? This clears its model and prompt, and re-enables it."),
                row.Name),
            Rhino.UI.LOC.STR("Reset Agent"),
            MessageBoxButtons.YesNo,
            MessageBoxType.Question);
        if (confirm != DialogResult.Yes)
            return;

        row.Model = string.Empty;
        row.SystemPrompt = string.Empty;
        row.Enabled = true;

        ReloadGrid();
        LoadEditor();
    }

    private void LoadEditor()
    {
        Loading = true;
        try
        {
            if (TryGetSelected(out AgentRow row))
            {
                NameHeader.Text = row.Name;
                AvailableLabel.Text = row.Available ? "✓ Found on search paths" : "✗ Not found on search paths";
                AvailableLabel.TextColor = row.Available ? Colors.Green : Colors.Red;
                EnabledBox.Checked = row.Enabled;
                LoadModelBox(row);
                SearchPathsBox.Text = row.SearchPathsText;
                SystemPromptBox.Text = row.SystemPrompt;
                EnableEditor(true);
            }
            else
            {
                NameHeader.Text = Rhino.UI.LOC.STR("No agent selected");
                AvailableLabel.Text = string.Empty;
                EnabledBox.Checked = false;
                ModelBox.Items.Clear();
                SearchPathsBox.Text = string.Empty;
                SystemPromptBox.Text = string.Empty;
                EnableEditor(false);
            }
        }
        finally { Loading = false; }
    }

    // Fills the editor's Model dropdown for the selected row: the "(default)" sentinel first, then the
    // adapter's choices. The box stays editable so a model not in the list can be typed and is remembered
    // on save. Only ever called under the Loading guard, so the resulting TextChanged does not write back.
    private void LoadModelBox(AgentRow row)
    {
        ModelBox.Items.Clear();
        ModelBox.Items.Add(new ListItem
        {
            Text = row.DefaultModel.Length > 0 ? $"{DefaultModelLabel} - {row.Models.FirstOrDefault(spec => spec.Id == row.DefaultModel)?.Display ?? row.DefaultModel}" : DefaultModelLabel,
            Key = string.Empty,
        });

        foreach (ModelSpec spec in row.Models)
            ModelBox.Items.Add(new ListItem { Text = spec.Display, Key = spec.Id });

        ModelBox.SelectedKey = row.Model;
    }

    // The "(default)" sentinel carries an empty key, so picking it falls back to the definition's own model.
    private string CurrentModelValue() => ModelBox.SelectedKey ?? string.Empty;

    private void EnableEditor(bool enabled)
    {
        EnabledBox.Enabled = enabled;
        ModelBox.Enabled = enabled;
        SystemPromptBox.Enabled = enabled;
    }

    private void WriteEditor(Action<AgentRow> apply)
    {
        if (Loading)
            return;
        if (TryGetSelected(out AgentRow row))
            apply(row);
    }

    private bool TryGetSelected(out AgentRow row)
    {
        if (AgentGrid.SelectedItem is AgentRow selected)
        {
            row = selected;
            return true;
        }
        row = default!;
        return false;
    }

    private void SetSelectedDefault()
    {
        if (!TryGetSelected(out AgentRow row))
            return;
        foreach (AgentRow other in Rows)
            other.IsDefault = false;
        row.IsDefault = true;
        ReloadGrid();
    }

    private void ReloadGrid()
    {
        if (Rows.Count == 0)
            return;
        AgentGrid.ReloadData(new Eto.Forms.Range<int>(0, Rows.Count - 1));
    }

    private Control McpServersTab()
    {
        McpJsonBox.Text = PrettyJson(AISettings.ExtraMcpServersJson);

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = Rhino.UI.LOC.STR("Extra MCP servers merged into every agent alongside the built-in \"rhino\" server."),
            TextColor = Colors.Gray,
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(help),
                new TableRow(McpJsonBox) { ScaleHeight = true },
                new TableRow(McpErrorLabel),
            },
        };
    }

    private Control ToolsTab()
    {
        // Two questions, asked in that order: how much of Rhino does this assistant reach, and then
        // how dangerous is each thing it reaches. The first is the two tables; the second is the
        // groups inside each of them.
        Control? own = Section(OwnCategory, ToolCatalog.All.Where(t => ToolPolicy.Owner(t.Name) == Profile));
        Control? shared = Section(SharedCategory, ToolCatalog.All.Where(t => ToolPolicy.Owner(t.Name) != Profile));

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = string.Format(
                Rhino.UI.LOC.STR("What the {0} panel's agent is allowed to do. On: it may call the tool. Ask: it has to "
                    + "ask you in the chat before each call. The other panels and external MCP clients are "
                    + "not affected."),
                AIProfiles.Name(Profile)),
            TextColor = Colors.Gray,
        };

        // An assistant that owns no tools of its own — the Rhino one — is left with the one table
        // rather than an empty heading. The divider is draggable because the two tables are never
        // anywhere near the same length.
        Control body = own is null || shared is null
            ? (own ?? shared)!
            : new Splitter
            {
                Orientation = Orientation.Vertical,
                FixedPanel = SplitterFixedPanel.Panel1,
                Panel1 = own,
                Panel2 = shared,
                Position = OwnSectionHeight,
            };

        // An override is only stored where the user departed from the default of the day, so settings
        // saved before a default changed go on winning over the new one. This is the way back.
        Button reset = new() { Text = Rhino.UI.LOC.STR("Reset to defaults") };
        reset.Click += (_, _) =>
        {
            AISettings.ClearToolModeOverrides(Profile);

            foreach (ToolNode leaf in ToolLeaves)
            {
                if (leaf.Tool is not { } tool)
                    continue;
                ToolMode mode = ToolPolicy.DefaultMode(Profile, tool);
                leaf.SetValue(0, mode != ToolMode.Off);
                leaf.SetValue(1, mode == ToolMode.Ask);
            }

            foreach (ToolNode group in ToolLeaves.Select(leaf => leaf.Parent).OfType<ToolNode>().Distinct())
                SyncGroupState(group);

            foreach (TreeGridView tree in Trees)
                tree.ReloadData();
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(help),
                new TableRow(body) { ScaleHeight = true },
                new TableRow(new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Items = { new StackLayoutItem(null, expand: true), reset },
                }),
            },
        };
    }

    // The permission tables, so a reset can repaint them.
    private List<TreeGridView> Trees { get; } = [];

    // One titled table: everything on one side of the first question, grouped by how dangerous it is.
    // Null when this assistant has nothing on that side.
    private Control? Section(string title, IEnumerable<ToolInfo> tools)
    {
        TreeGridItemCollection roots = [];
        foreach (IGrouping<string, ToolInfo> behaviour in tools
                     .GroupBy(t => t.Behaviour)
                     .OrderBy(g => CategoryOrder(g.Key)))
        {
            ToolNode group = new(CategoryLabel(behaviour.Key)) { Expanded = true };
            foreach (ToolInfo tool in behaviour.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            {
                ToolNode leaf = new(tool, ToolPolicy.Mode(Profile, tool)) { Parent = group };
                group.Children.Add(leaf);
                ToolLeaves.Add(leaf);
            }
            SyncGroupState(group);
            roots.Add(group);
        }

        if (roots.Count == 0)
            return null;

        Label label = new() { Text = title, Font = SystemFonts.Bold() };

        return new TableLayout
        {
            Spacing = new Size(0, 4),
            Rows =
            {
                new TableRow(label),
                new TableRow(Tree(roots)) { ScaleHeight = true },
            },
        };
    }

    // Tool before Ask, which is not the order the two switches would like: the tree draws its
    // expander and its indent in the first column, so anything between that and the name leaves a
    // group row reading as an expander, a checkbox, an empty checkbox and then, at a distance, a
    // word — which is not a heading. Next to its own checkbox it is one.
    private TreeGridView Tree(TreeGridItemCollection roots)
    {
        TreeGridView tree = new() { ShowHeader = true, DataStore = roots };
        Trees.Add(tree);
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("On"), DataCell = new CheckBoxCell(0), Editable = true, Width = 44 });
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("Tool"), DataCell = new TextBoxCell(2), Width = 230 });
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("Ask"), DataCell = new CheckBoxCell(1), Editable = true, Width = 48 });
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("Description"), DataCell = new TextBoxCell(3), Width = 380 });

        // And the group's own name in bold, so the eye has something to catch besides the indent.
        tree.CellFormatting += (_, e) =>
        {
            if (e.Item is ToolNode { IsGroup: true })
                e.Font = SystemFonts.Bold();
        };

        tree.CellEdited += (_, e) =>
        {
            if (e.Item is not ToolNode node)
                return;

            // The columns moved; the values did not. On and Ask are still 0 and 1.
            int column = e.Column == 2 ? 1 : e.Column;
            if (column > 1)
                return;

            // A group reaches every tool under it, however deep; every group above it then re-reads
            // its own state from what is now there.
            if (node.IsGroup)
            {
                Cascade(node, column, node.GetValue(column) is true);
                tree.ReloadItem(node);
            }

            for (ToolNode? above = node.Parent as ToolNode; above is not null; above = above.Parent as ToolNode)
            {
                SyncGroupState(above);
                tree.ReloadItem(above);
            }
        };

        return tree;
    }

    private static void Cascade(ToolNode group, int column, bool value)
    {
        foreach (ToolNode child in group.Children.OfType<ToolNode>())
        {
            child.SetValue(column, value);
            if (child.IsGroup)
                Cascade(child, column, value);
        }
    }

    // A group checkbox is checked only when every tool under it has it; toggling it cascades to all
    // children. Mixed groups read as unchecked (no tri-state) to keep the model free of nulls.
    private static void SyncGroupState(ToolNode group)
    {
        List<ToolNode> children = group.Children.OfType<ToolNode>().ToList();
        group.SetValue(0, children.Count > 0 && children.All(c => c.GetValue(0) is true));
        group.SetValue(1, children.Count > 0 && children.All(c => c.GetValue(1) is true));
    }

    // The assistant's own tools are few and the shared ones are many, so the divider starts here
    // rather than halfway.
    private const int OwnSectionHeight = 200;

    // The first question is how much of Rhino this assistant reaches, which is now which table a
    // tool is in.
    private static string OwnCategory => Rhino.UI.LOC.STR("Relevant to this assistant");
    private static string SharedCategory => Rhino.UI.LOC.STR("General access to Rhino resources");

    private static string CategoryLabel(string category) => category switch
    {
        "Read-only" => Rhino.UI.LOC.STR("Read-only"),
        "Modify" => Rhino.UI.LOC.STR("Modify"),
        "Destructive" => Rhino.UI.LOC.STR("Destructive"),
        _ => category,
    };

    private static int CategoryOrder(string category) => category switch
    {
        "Read-only" => 0,
        "Modify" => 1,
        "Destructive" => 2,
        _ => 3,
    };

    private static string PrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptyMcpJson;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static bool TryValidateMcpJson(string json, out string normalized, out string error)
    {
        normalized = EmptyMcpJson;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = Rhino.UI.LOC.STR("MCP config must be a JSON object.");
                return false;
            }
            if (!doc.RootElement.TryGetProperty("mcpServers", out JsonElement servers)
                || servers.ValueKind != JsonValueKind.Object)
            {
                error = Rhino.UI.LOC.STR("MCP config must contain an \"mcpServers\" object.");
                return false;
            }
            normalized = JsonSerializer.Serialize(doc.RootElement, IndentedJson);
            return true;
        }
        catch (JsonException ex)
        {
            error = string.Format(Rhino.UI.LOC.STR("Invalid JSON: {0}"), ex.Message);
            return false;
        }
    }

    // A row of a permissions tree. Group nodes carry the category label in value 2 and roll-up
    // checkboxes in 0 (On) and 1 (Ask); leaf nodes carry [on, ask, title, description].
    private sealed class ToolNode : TreeGridItem
    {
        public ToolInfo? Tool { get; }
        public bool IsGroup => Tool is null;

        public ToolNode(ToolInfo tool, ToolMode mode)
            : base(mode != ToolMode.Off, mode == ToolMode.Ask, tool.Title, tool.Description)
        {
            Tool = tool;
        }

        public ToolNode(string category)
            : base(false, false, category, string.Empty)
        {
        }

        public ToolMode Mode =>
            GetValue(0) is not true ? ToolMode.Off : GetValue(1) is true ? ToolMode.Ask : ToolMode.On;
    }

    // Only Model, SystemPrompt, Enabled and IsDefault are user-editable; the rest mirrors the definition.
    private sealed class AgentRow
    {
        public string Name { get; }
        public bool Available { get; }
        public string SearchPathsText { get; }
        public IReadOnlyList<ModelSpec> Models { get; }
        public string DefaultModel { get; }

        public string Model { get; set; }
        public string SystemPrompt { get; set; }
        public bool Enabled { get; set; }
        public bool IsDefault { get; set; }

        public string DefaultGlyph => IsDefault ? "★" : string.Empty;

        public AgentRow(
            string name, bool available, string searchPathsText, IReadOnlyList<ModelSpec> models,
            string defaultModel, string model, string systemPrompt, bool enabled, bool isDefault)
        {
            Name = name;
            Available = available;
            SearchPathsText = searchPathsText;
            Models = models;
            DefaultModel = defaultModel;
            Model = model;
            SystemPrompt = systemPrompt;
            Enabled = enabled;
            IsDefault = isDefault;
        }

        public static AgentRow From(AgentDefinition def, bool isDefault) =>
            new(
                def.Name,
                def.Available,
                string.Join(Environment.NewLine, def.SearchPaths.GetPaths()),
                def.Models,
                def.DefaultModel,
                AISettings.AgentModel(def.Name),
                AISettings.AgentPrompt(def.Name),
                AISettings.IsEnabled(def),
                isDefault);
    }
}
