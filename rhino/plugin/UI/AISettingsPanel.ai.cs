using System.Collections.ObjectModel;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI;

// Shared settings UI (AI Agents / MCP Servers / Tools). Hosted by both the standalone
// AISettingsDialog and the Rhino Options page (AIOptionsPage); neither owns the commit logic,
// it lives here behind TryCommit so the two hosts can never drift.
internal sealed class AISettingsPanel : Panel
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

    public AISettingsPanel()
    {
        Padding = new Padding(20);
        Height = 600;

        SeedRows();

        TabControl tabs = new();
        tabs.Pages.Add(new TabPage { Text = Rhino.UI.LOC.STR("AI Agents"), Content = AgentsTab() });
        tabs.Pages.Add(new TabPage { Text = Rhino.UI.LOC.STR("MCP Servers"), Content = McpServersTab() });
        tabs.Pages.Add(new TabPage { Text = Rhino.UI.LOC.STR("Tools"), Content = ToolsTab() });

        Content = tabs;
    }

    // Persists every tab back to AISettings. Returns false (and shows the MCP error inline) when the
    // MCP JSON is invalid, so the hosting dialog/page can keep itself open; true once everything is saved.
    public bool TryCommit(out string error)
    {
        error = string.Empty;

        if (!TryValidateMcpJson(McpJsonBox.Text, out string normalizedJson, out string validationError))
        {
            McpErrorLabel.Text = validationError;
            McpErrorLabel.Visible = true;
            error = validationError;
            return false;
        }
        McpErrorLabel.Visible = false;

        foreach (AgentRow row in Rows)
        {
            AISettings.SetAgentEnabled(row.Name, row.Enabled);
            AISettings.SetAgentModel(row.Name, row.Model);
            AISettings.SetAgentPrompt(row.Name, row.SystemPrompt);
        }

        if (Rows.FirstOrDefault(r => r.IsDefault) is AgentRow defaultRow)
            AISettings.DefaultAgentName = defaultRow.Name;

        AISettings.ExtraMcpServersJson = normalizedJson;

        // ScanTools hides router-internal underscore tools from the grid, so they have no checkbox to
        // round-trip; carry forward any that were already disabled instead of silently dropping them.
        IEnumerable<string> uncheckedNames = ToolLeaves
            .Where(leaf => leaf.GetValue(0) is not true)
            .Select(leaf => leaf.ToolName);
        IEnumerable<string> preservedUnderscore = AISettings.DisabledTools
            .Where(n => n.StartsWith('_'));
        AISettings.DisabledTools = uncheckedNames
            .Concat(preservedUnderscore)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return true;
    }

    private void SeedRows()
    {
        string defaultName = AISettings.DefaultAgentName;
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

        TableLayout layout = new()
        {
            Padding = new Padding(8),
            Spacing = new Size(12, 0),
            Rows =
            {
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
            this,
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
        HashSet<string> disabled = new(AISettings.DisabledTools, StringComparer.OrdinalIgnoreCase);

        TreeGridItemCollection roots = [];
        foreach (IGrouping<string, ToolInfo> group in ScanTools()
                     .GroupBy(t => t.Category)
                     .OrderBy(g => CategoryOrder(g.Key)))
        {
            ToolNode groupNode = new(CategoryLabel(group.Key)) { Expanded = true };
            foreach (ToolInfo tool in group.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            {
                ToolNode leaf = new(tool.Name, !disabled.Contains(tool.Name), tool.Title, tool.Description)
                {
                    Parent = groupNode,
                };
                groupNode.Children.Add(leaf);
                ToolLeaves.Add(leaf);
            }
            SyncGroupState(groupNode);
            roots.Add(groupNode);
        }

        TreeGridView tree = new() { ShowHeader = true, DataStore = roots };
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("On"), DataCell = new CheckBoxCell(0), Editable = true, Width = 44 });
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("Tool"), DataCell = new TextBoxCell(1), Width = 210 });
        tree.Columns.Add(new GridColumn { HeaderText = Rhino.UI.LOC.STR("Description"), DataCell = new TextBoxCell(2), Width = 380 });
        tree.CellEdited += (_, e) =>
        {
            if (e.Column != 0 || e.Item is not ToolNode node)
                return;
            if (node.IsGroup)
            {
                bool on = node.GetValue(0) is true;
                foreach (ToolNode child in node.Children.OfType<ToolNode>())
                    child.SetValue(0, on);
                tree.ReloadItem(node);
            }
            else if (node.Parent is ToolNode group)
            {
                SyncGroupState(group);
                tree.ReloadItem(group);
            }
        };

        Label help = new()
        {
            Wrap = WrapMode.Word,
            Text = Rhino.UI.LOC.STR("Tools the built-in \"rhino\" server exposes, grouped by behaviour. Unchecking a tool hides it from in-Rhino agents only; external clients still see every tool."),
            TextColor = Colors.Gray,
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(help),
                new TableRow(tree) { ScaleHeight = true },
            },
        };
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Read-only" => 0,
        "Modify" => 1,
        "Destructive" => 2,
        _ => 3,
    };

    private static string CategoryLabel(string category) => category switch
    {
        "Read-only" => Rhino.UI.LOC.STR("Read-only"),
        "Modify" => Rhino.UI.LOC.STR("Modify"),
        "Destructive" => Rhino.UI.LOC.STR("Destructive"),
        _ => category,
    };

    // A group checkbox is checked only when every tool under it is on; toggling it cascades to all
    // children. Mixed groups read as unchecked (no tri-state) to keep the model free of nulls.
    private static void SyncGroupState(ToolNode group)
    {
        List<ToolNode> children = group.Children.OfType<ToolNode>().ToList();
        bool allOn = children.Count > 0 && children.All(c => c.GetValue(0) is true);
        group.SetValue(0, allOn);
    }

    // Mirror of ToolRegistry.Scan that reads name/title/description/behaviour without instantiating
    // tools or needing an IServiceProvider (Scan does the latter to build full schemas, which we must
    // not do here). Router-internal tools (leading underscore) are excluded so they can't be hidden.
    private static IReadOnlyList<ToolInfo> ScanTools()
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        List<ToolInfo> tools = [];
        Assembly assembly = typeof(McpSerializer).Assembly;
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not McpServerToolAttribute toolAttr)
                    continue;

                string name = toolAttr.Name ?? method.Name;
                if (name.StartsWith('_'))
                    continue;

                string title = string.IsNullOrWhiteSpace(toolAttr.Title) ? name : toolAttr.Title!;
                string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                string category = toolAttr.ReadOnly ? "Read-only" : toolAttr.Destructive ? "Destructive" : "Modify";
                tools.Add(new ToolInfo(name, title, description, category));
            }
        }

        return tools;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

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

    // Immutable scan result for one tool row in the Tools tree.
    private readonly record struct ToolInfo(string Name, string Title, string Description, string Category);

    // TreeGridView node for the Tools tab. Group nodes carry the category label in column 1 and a
    // roll-up checkbox in column 0; leaf nodes carry [enabled, title, description] and the tool name.
    private sealed class ToolNode : TreeGridItem
    {
        public string ToolName { get; }
        public bool IsGroup { get; }

        public ToolNode(string toolName, bool enabled, string title, string description)
            : base(enabled, title, description)
        {
            ToolName = toolName;
            IsGroup = false;
        }

        public ToolNode(string category)
            : base(false, category, string.Empty)
        {
            ToolName = string.Empty;
            IsGroup = true;
        }
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
