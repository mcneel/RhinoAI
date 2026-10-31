using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;

using Rhino.AI.Models;
using Rhino.AI.PlugIns;
using Rhino.Runtime;
using Rhino.UI;

namespace Rhino.AI.UI;

// Displays and allows for adding and deleting Agents
internal sealed class AIAgentDefinitionsPage : AIPage
{

    private AIAgentDefinitionsViewModel Model => (DataContext as AIAgentDefinitionsViewModel)!;

    public AIAgentDefinitionsPage()
    {
        DataContext = new AIAgentDefinitionsViewModel();
        InitLayout();
        Model.PropertyChanged += UpdateGrid;
    }

    private void UpdateGrid(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UIAgentDefinition.Preferred)) return;
        Invalidate(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Model.Update();
    }

    private void InitLayout()
    {
        Splitter splitter = new()
        {
            AllowDrop = false,
            Panel1 = GetDefinitionsView(),
            Panel2 = GetProperties(),
            Panel2MinimumSize = 400,
            FixedPanel = SplitterFixedPanel.Panel1,
        };

        Content = splitter;
    }

    private static Padding InternalPadding { get; } = new(4, 4);
    private static Size InternalSpacing { get; } = new(6, 6);

    private Control GetProperties()
    {
        DynamicLayout layout = new() { Padding = InternalPadding, Spacing = InternalSpacing };

        // NOTE : Someone WILL say "Why aren't we using OptionsList here?" - It seems to be internal, so I can't

        CheckBox enabled = new();
        enabled.BindDataContext(c => c.Checked, Binding.Property((UIAgentDefinition d) => d.Enabled).Cast<bool?>());

        CheckBox preferred = new();
        preferred.BindDataContext(c => c.Checked, Binding.Property((UIAgentDefinition d) => d.Preferred).Cast<bool?>());

        Label available = new();
        available.BindDataContext(
            l => l.Text,
            Binding.Property((UIAgentDefinition d) => d.Available).Convert(a => (string?)(a ? "Available" : "Not available")),
            DualBindingMode.OneWay);

        layout.BeginVertical(Padding.Empty, InternalSpacing);
        layout.AddRow(Caption("Name"), BindText(new TextBox(), d => d.DisplayName));
        layout.AddRow(Caption("Vendor"), BindText(new TextBox(), d => d.Vendor));
        layout.AddRow(Caption("Enabled"), enabled);
        layout.AddRow(Caption("Preferred"), preferred);
        layout.AddRow(Caption("Status"), available);
        layout.AddRow(Caption("Default model"), GetDefaultModelDropDown());
        layout.EndVertical();

        layout.Add(new Label { Text = "Default prompt" });
        layout.Add(BindText(new TextArea { Wrap = true, Height = 90 }, d => d.DefaultPrompt));

        layout.Add(new Label { Text = "Models" });
        layout.Add(GetModelsEditor());

        layout.Add(new Rhino.UI.Controls.LabelSeparator());

        // Definition specific section

        DropDown typeDropDown = new()
        {
            DataStore = new ObservableCollection<object>(AIAgentDefinitionsViewModel.Types),
        };
        typeDropDown.BindDataContext(tdd => tdd.SelectedValue, Binding.Property((UIAgentDefinition d) => d.Type).Cast<object?>());
        layout.BeginVertical(Padding.Empty, InternalSpacing);
        layout.AddRow(Caption("Type"), typeDropDown);
        layout.EndVertical();

        Panel customControls = new() { Padding = 0 };
        customControls.BindDataContext(cc => cc.Content, Binding.Property((UIAgentDefinition d) => d.Type).Convert(t => (Control?)GetTypeControls(t)));
        layout.Add(customControls);

        layout.Add(null);

        layout.DataContext = Model.Current;
        layout.Enabled = Model.Current is not null;
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AIAgentDefinitionsViewModel.Current)) return;
            layout.DataContext = Model.Current;
            layout.Enabled = Model.Current is not null;
        };

        return new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true };
    }

    // Captions are split across several tables, so a shared width keeps their value columns aligned
    private const int CaptionWidth = 150;

    private static Label Caption(string text) => new() { Text = text, Width = CaptionWidth, VerticalAlignment = VerticalAlignment.Center };

    private static Control BindText(TextControl control, Expression<Func<UIAgentDefinition, string?>> property)
    {
        control.TextBinding.BindDataContext(property);
        return control;
    }

    private static Control GetDefaultModelDropDown()
    {
        DropDown defaultModel = new()
        {
            ItemKeyBinding = Binding.Property((UIModelDefinition m) => m.ModelId),
            ItemTextBinding = Binding.Property((UIModelDefinition m) => m.DisplayName),
        };
        defaultModel.BindDataContext(
            dd => dd.DataStore,
            Binding.Property((UIAgentDefinition d) => d.AvailableModels).Convert(models => (IEnumerable<object>?)models),
            DualBindingMode.OneWay);
        defaultModel.BindDataContext(
            dd => dd.SelectedKey,
            Binding.Property((UIAgentDefinition d) => d.DefaultModelId).Cast<string?>(),
            DualBindingMode.OneWay);
        defaultModel.SelectedKeyChanged += (_, _) =>
        {
            if (defaultModel.SelectedKey is string key && defaultModel.DataContext is UIAgentDefinition definition)
            {
                definition.DefaultModelId = key;
            }
        };

        return defaultModel;
    }

    private Control GetModelsEditor()
    {
        GridView grid = new()
        {
            Height = 120,
            Border = BorderType.Line,
            AllowEmptySelection = true,
            AllowMultipleSelection = false,
            AllowColumnReordering = false,

            Columns =
            {
                new GridColumn
                {
                    HeaderText = "Name",
                    DataCell = new TextBoxCell(nameof(UIModelDefinition.DisplayName)),
                    Editable = true,
                    Expand = true,
                },
                new GridColumn
                {
                    HeaderText = "Model Id",
                    DataCell = new TextBoxCell(nameof(UIModelDefinition.ModelId)),
                    Editable = true,
                    Expand = true,
                },
            },
        };
        grid.BindDataContext(
            g => g.DataStore,
            Binding.Property((UIAgentDefinition d) => d.AvailableModels).Convert(models => (IEnumerable<object>?)models),
            DualBindingMode.OneWay);
        grid.ContextMenu = GetModelsContextMenu(grid);

        ButtonSegmentedItem add = new() { Text = "+" };
        add.Click += (_, _) => AddModel(grid);

        ButtonSegmentedItem remove = new() { Text = "-", Enabled = false };
        remove.Click += (_, _) => RemoveSelectedModel(grid);

        grid.SelectedItemsChanged += (_, _) => remove.Enabled = grid.SelectedItem is UIModelDefinition;

        SegmentedButton addRemove = new() { Items = { add, remove } };

        DynamicLayout layout = new();
        layout.BeginVertical();
        layout.Add(grid, yscale: true);
        layout.AddSeparateRow(addRemove, null);
        layout.EndVertical();

        return layout;
    }

    private ContextMenu GetModelsContextMenu(GridView grid)
    {
        ButtonMenuItem delete = new() { Text = "Delete" };
        delete.Click += (_, _) => RemoveSelectedModel(grid);

        ButtonMenuItem duplicate = new() { Text = "Duplicate" };
        duplicate.Click += (_, _) => DuplicateSelectedModel(grid);

        ContextMenu menu = new() { Items = { delete, duplicate } };
        menu.Opening += (_, _) =>
        {
            bool hasSelection = grid.SelectedItem is UIModelDefinition;
            delete.Enabled = hasSelection;
            duplicate.Enabled = hasSelection;
        };
        return menu;
    }

    private static void AddModel(GridView grid)
    {
        if (grid.DataContext is not UIAgentDefinition definition) return;

        definition.AvailableModels.Add(new UIModelDefinition());
        int row = definition.AvailableModels.Count - 1;
        grid.SelectedRow = row;
        grid.ScrollToRow(row);
        grid.BeginEdit(row, 0);
    }

    private void RemoveSelectedModel(GridView grid)
    {
        if (grid.DataContext is not UIAgentDefinition definition) return;
        if (grid.SelectedItem is not UIModelDefinition model) return;

        DialogResult confirm = MessageBox.Show(
            this,
            $"Remove \"{model.DisplayName}\" from {definition.DisplayName}?",
            "Remove Model",
            MessageBoxButtons.YesNo,
            MessageBoxType.Question);
        if (confirm != DialogResult.Yes) return;

        definition.AvailableModels.Remove(model);
    }

    private static void DuplicateSelectedModel(GridView grid)
    {
        if (grid.DataContext is not UIAgentDefinition definition) return;
        if (grid.SelectedItem is not UIModelDefinition model) return;

        int index = definition.AvailableModels.IndexOf(model) + 1;
        definition.AvailableModels.Insert(index, model.Duplicate());
        grid.SelectedRow = index;
    }

    private static DynamicLayout GetTypeControls(AgentType type)
    {
        (string Caption, Control Control)[] fields = type switch
        {
            AgentType.Unknown => [],
            AgentType.Cli =>
            [
                ("Windows search paths", BindText(new TextArea { Wrap = false, Height = 60 }, d => d.WinSearchPaths)),
                ("Mac search paths", BindText(new TextArea { Wrap = false, Height = 60 }, d => d.MacSearchPaths)),
            ],
            AgentType.Local =>
            [
                ("Host", BindText(new TextBox { PlaceholderText = "http://localhost:1234" }, d => d.Host)),
                ("Protocol", GetProtocolDropDown()),
                ("Models route", BindText(new TextBox { PlaceholderText = "/v1/models" }, d => d.ModelsRoute)),
            ],
            AgentType.Api =>
            [
                ("Host", BindText(new TextBox { PlaceholderText = "https://" }, d => d.Host)),
                ("Protocol", GetProtocolDropDown()),
                ("API key", BindText(new PasswordBox(), d => d.ApiKey)),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        DynamicLayout layout = new();
        layout.BeginVertical(Padding.Empty, InternalSpacing);
        foreach ((string caption, Control control) in fields)
        {
            layout.AddRow(Caption(caption), control);
        }
        layout.EndVertical();

        return layout;
    }

    private static Control GetProtocolDropDown()
    {
        EnumDropDown<ProtocolKind> dropDown = new();
        dropDown.SelectedValueBinding.BindDataContext((UIAgentDefinition d) => d.Protocol);
        return dropDown;
    }

    private Control GetDefinitionsView()
    {
        GridView grid = new()
        {
            AllowDrop = false,
            ShowHeader = false,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            AllowMultipleSelection = false,
            CanDeleteItem = (_) => false,
            DataStore = Model.Definitions,

            Columns =
            {
                new GridColumn()
                {
                    DataCell = new ImageViewCell(nameof(UIAgentDefinition.Icon)),
                    Editable = false,
                },
                new GridColumn()
                {
                    HeaderText = "Name",
                    DataCell = new TextBoxCell(nameof(UIAgentDefinition.DisplayName)),
                    Expand = true,
                },
                new GridColumn()
                {
                    HeaderText = "Enabled",
                    DataCell = new CheckBoxCell(nameof(UIAgentDefinition.Enabled)),
                    Editable = true,
                },
                new GridColumn()
                {
                    DataCell = new ImageViewCell(nameof(UIAgentDefinition.PreferredImage)),
                    CellToolTipBinding = Binding.Property((UIAgentDefinition d) => d.PreferredToolTip),
                    Editable = false,
                },
            }
        };

        grid.SelectedRow = Model.Index;
        grid.SelectedRowsChanged += (_, _) => Model.Index = grid.SelectedRow;
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AIAgentDefinitionsViewModel.Index)) return;
            grid.SelectedRow = Model.Index;
        };

        SegmentedButton addRemove = new()
        {
            Items =
            {
                new ButtonSegmentedItem(Model.AddCommand) { Text = "Add" },
                new ButtonSegmentedItem(Model.RemoveCommand) { Text = "Remove" },
            },
        };

        DynamicLayout layout = new() { Width = 180 };
        layout.BeginVertical();
        layout.Add(new Label() { Text = "Agent Definitions" });
        layout.Add(grid, yscale: true);
        layout.AddSeparateColumn(addRemove, null);
        layout.EndVertical();

        return layout;
    }

    public override bool Validate() => Model.FindProblem() is null;

    public override bool TryApplyChanges() => Model.TryApplyChanges();

}

internal sealed class AIAgentDefinitionsViewModel : SimpleViewModel
{

    public ObservableCollection<UIAgentDefinition> Definitions { get; } = [];

    private int index = -1;
    public int Index
    {
        get => index;
        set
        {
            if (SetProperty(ref index, value))
            {
                RaisePropertyChanged(nameof(Current));
            }
        }
    }

    public UIAgentDefinition? Current => Definitions.ElementAtOrDefault(Index);

    public Command AddCommand { get; }
    public Command RemoveCommand { get; }

    public static List<object> Types { get; } = [AgentType.Cli, AgentType.Api, AgentType.Local];

    private HashSet<Guid> AppliedIds { get; set; } = [];

    public AIAgentDefinitionsViewModel()
    {
        AddCommand = new Command(AddMethod);
        RemoveCommand = new Command(RemoveMethod);
        Update();
    }

    public void Update()
    {
        Definitions.Clear();
        foreach (IAgentDefinition definition in Instances.Agents.Definitions)
        {
            UIAgentDefinition newDef = new UIAgentDefinition(definition);
            Definitions.Add(newDef);

            newDef.PropertyChanged += UpdateGrid;
        }

        AppliedIds = [.. Instances.Agents.Definitions.Select(d => d.Id)];
        Index = Definitions.Count > 0 ? 0 : -1;
        RaisePropertyChanged(nameof(Current));
    }

    private void UpdateGrid(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UIAgentDefinition.Preferred)) return;
        this.RaisePropertyChanged(nameof(UIAgentDefinition.Preferred));
    }

    private void AddMethod(object? sender, EventArgs e)
    {
        Definitions.Add(new UIAgentDefinition());
        Index = Definitions.Count - 1;
    }

    private void RemoveMethod(object? sender, EventArgs e)
    {
        if (Current is not UIAgentDefinition current) return;

        DialogResult confirm = MessageBox.Show(
            $"Remove the agent definition \"{current.DisplayName}\"?",
            "Remove Agent Definition",
            MessageBoxButtons.YesNo,
            MessageBoxType.Question,
            MessageBoxDefaultButton.No);
        if (confirm != DialogResult.Yes) return;

        int removed = Index;
        Definitions.RemoveAt(removed);
        Index = Math.Min(removed, Definitions.Count - 1);
        RaisePropertyChanged(nameof(Current));
    }

    public DefinitionProblem? FindProblem()
    {
        foreach (UIAgentDefinition definition in Definitions)
        {
            if (FindProblem(definition) is DefinitionProblemKind kind)
            {
                return new(definition, kind);
            }
        }

        return null;
    }

    private static DefinitionProblemKind? FindProblem(UIAgentDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DisplayName)) return DefinitionProblemKind.MissingDisplayName;
        if (string.IsNullOrWhiteSpace(definition.Vendor)) return DefinitionProblemKind.MissingVendor;
        if (FindTypeProblem(definition) is DefinitionProblemKind typeProblem) return typeProblem;

        bool hasDefaultModel = !string.IsNullOrWhiteSpace(definition.DefaultModelId);
        if (hasDefaultModel && !definition.AvailableModels.Any(m => string.Equals(m.ModelId, definition.DefaultModelId, StringComparison.OrdinalIgnoreCase)))
        {
            return DefinitionProblemKind.UnknownDefaultModel;
        }

        return null;
    }

    private static DefinitionProblemKind? FindTypeProblem(UIAgentDefinition definition) => definition.Type switch
    {
        AgentType.Unknown => DefinitionProblemKind.UnknownType,
        AgentType.Cli when string.IsNullOrWhiteSpace(definition.WinSearchPaths) && string.IsNullOrWhiteSpace(definition.MacSearchPaths) => DefinitionProblemKind.MissingSearchPath,
        AgentType.Cli => null,
        AgentType.Api or AgentType.Local when !IsHttpUri(definition.Host) => DefinitionProblemKind.InvalidHost,
        AgentType.Local when string.IsNullOrWhiteSpace(definition.ModelsRoute) => DefinitionProblemKind.MissingModelsRoute,
        AgentType.Api or AgentType.Local => null,

        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "Unknown agent type."),
    };

    private static bool IsHttpUri(string? host)
        => Uri.TryCreate(host, UriKind.Absolute, out Uri? uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public bool TryApplyChanges()
    {
        if (FindProblem() is not null) return false;

        List<IAgentDefinition> built = new(Definitions.Count);
        foreach (UIAgentDefinition definition in Definitions)
        {
            if (definition.GetDefinition() is not IAgentDefinition agent) return false;
            built.Add(agent);
        }

        HashSet<Guid> keptIds = [.. built.Select(d => d.Id)];
        foreach (Guid removedId in AppliedIds.Except(keptIds))
        {
            Instances.Agents.Remove(removedId);
        }

        bool result = true;
        foreach (IAgentDefinition agent in built)
        {
            Instances.Agents.Remove(agent.Id);
            result &= Instances.Agents.Add(agent);
        }

        AppliedIds = keptIds;
        return result;
    }

}

internal enum DefinitionProblemKind
{
    UnknownType,
    MissingDisplayName,
    MissingVendor,
    MissingSearchPath,
    InvalidHost,
    MissingModelsRoute,
    UnknownDefaultModel,
}

internal sealed record DefinitionProblem(UIAgentDefinition Definition, DefinitionProblemKind Kind);

internal sealed class UIAgentDefinition : SimpleViewModel
{

    private const string DefaultModelsRoute = "/v1/models";

    private static char[] LineBreaks { get; } = ['\r', '\n'];

    public Guid Id { get; } = Guid.NewGuid();

    private string vendor = "Custom";
    public string Vendor { get => vendor; set => SetProperty(ref vendor, value); }

    private string displayName = "New Agent";
    public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); }

    private string defaultPrompt = string.Empty;
    public string DefaultPrompt { get => defaultPrompt; set => SetProperty(ref defaultPrompt, value); }

    private string svg = string.Empty;
    public string Svg
    {
        get => svg;
        set
        {
            if (!SetProperty(ref svg, value)) return;
            Icon = CreateIcon(value);
            RaisePropertyChanged(nameof(Icon));
        }
    }

    public Icon? Icon { get; private set; }

    public bool Available { get; }

    private bool enabled = true;
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }

    public bool Preferred
    {
        get => Instances.Preferences.PreferedAgentId == this.Id;
        set
        {
            Instances.Preferences.PreferedAgentId = this.Id;
            // TODO : Does this refresh other items?
            RaisePropertyChanged(nameof(Preferred));
        }
    }

    private const string PreferredIconResourceName = "Rhino.AI.Panel_light.svg";
    private const string PreferredDarkIconResourceName = "Rhino.AI.Panel_dark.svg";
    private static Bitmap? PreferredLightImage { get; set; }
    private static Bitmap? PreferredDarkImage { get; set; }

    public Bitmap? PreferredImage
    {
        get
        {
            if (!Preferred) return null;
            return HostUtils.RunningInDarkMode
                ? PreferredDarkImage ??= LoadPreferredImage(PreferredDarkIconResourceName)
                : PreferredLightImage ??= LoadPreferredImage(PreferredIconResourceName);
        }
    }

    public string PreferredToolTip => LOC.STR("Preferred Agent");

    // The light and dark artwork are separate files, so the svg needs no dark-mode adjustment.
    private static Bitmap? LoadPreferredImage(string resourceName)
        => Rhino.UI.ImageResources.CreateEtoBitmap(resourceName, typeof(UIAgentDefinition).Assembly, 32, 32, false);

    private string defaultModelId = string.Empty;
    public string DefaultModelId { get => defaultModelId; set => SetProperty(ref defaultModelId, value); }

    private string? currentModelId;
    public string? CurrentModelId { get => currentModelId; set => SetProperty(ref currentModelId, value); }

    public PermissionSet Permissions { get; } = new();

    private AgentType type = AgentType.Api;
    public AgentType Type { get => type; set => SetProperty(ref type, value); }

    public ObservableCollection<UIModelDefinition> AvailableModels { get; } = [];

    private string winSearchPaths = string.Empty;
    public string WinSearchPaths { get => winSearchPaths; set => SetProperty(ref winSearchPaths, value); }

    private string macSearchPaths = string.Empty;
    public string MacSearchPaths { get => macSearchPaths; set => SetProperty(ref macSearchPaths, value); }

    private string? host = "https://";
    public string? Host { get => host; set => SetProperty(ref host, value); }

    private ProtocolKind protocol = ProtocolKind.Completions;
    public ProtocolKind Protocol { get => protocol; set => SetProperty(ref protocol, value); }

    private string? modelsRoute;
    public string? ModelsRoute { get => modelsRoute; set => SetProperty(ref modelsRoute, value); }

    private string? apiKeyEnvVar;
    public string? ApiKeyEnvVar { get => apiKeyEnvVar; set => SetProperty(ref apiKeyEnvVar, value); }
    
    private string? apiKey;
    public string? ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); }

    private Platform Platforms { get; } = Platform.All;

    // The loaded key may come from an environment variable; writing it back would copy it into the OS vault.
    private string? LoadedApiKey { get; }

    private bool IsApiKeyChanged => !string.Equals(NullIfEmpty(ApiKey), NullIfEmpty(LoadedApiKey), StringComparison.Ordinal);

    public UIAgentDefinition()
    {
    }

    public UIAgentDefinition(IAgentDefinition definition)
    {
        Id = definition.Id;
        vendor = definition.Vendor;
        displayName = definition.DisplayName;
        defaultPrompt = definition.DefaultPrompt;
        svg = definition.SvgIcon;
        Icon = CreateIcon(svg);
        Available = definition.Available;
        enabled = definition.Enabled;
        defaultModelId = definition.DefaultModelId;
        currentModelId = definition.CurrentModelId;
        CopyPermissions(definition.Permissions, Permissions);

        foreach (ModelDefinition model in definition.AvailableModels)
        {
            AvailableModels.Add(new UIModelDefinition(model));
        }

        type = definition switch
        {
            CliAgentDefinition => AgentType.Cli,
            HttpAgentDefinition { ModelsRoute: not null } => AgentType.Local,
            HttpAgentDefinition => AgentType.Api,
            _ => AgentType.Unknown,
        };

        switch (definition)
        {
            case CliAgentDefinition cli:
                Platforms = cli.Platforms;
                winSearchPaths = string.Join(Environment.NewLine, cli.WinSearchPaths);
                macSearchPaths = string.Join(Environment.NewLine, cli.MacSearchPaths);
                break;

            case HttpAgentDefinition http:
                Platforms = http.Platforms;
                host = http.Host;
                protocol = http.Protocol;
                modelsRoute = http.ModelsRoute;
                apiKey = http.ApiKey;
                apiKeyEnvVar = http.ApiKeyEnvVar;
                LoadedApiKey = apiKey;
                break;
        }
    }

    public IAgentDefinition? GetDefinition()
    {
        IAgentDefinition? definition = Type switch
        {
            AgentType.Cli => BuildCli(),
            AgentType.Api => BuildHttp(null),
            AgentType.Local => BuildHttp(string.IsNullOrWhiteSpace(ModelsRoute) ? DefaultModelsRoute : ModelsRoute.Trim()),
            AgentType.Unknown => null,
            _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null),
        };

        if (definition is null) return null;

        definition.Enabled = Enabled;
        definition.DefaultModelId = string.IsNullOrWhiteSpace(DefaultModelId) ? ModelDefinitions.FirstOrDefault()?.ModelId ?? string.Empty : DefaultModelId;
        definition.CurrentModelId = CurrentModelId;
        CopyPermissions(Permissions, definition.Permissions);

        if (definition is HttpAgentDefinition http && IsApiKeyChanged)
        {
            http.ApiKey = NullIfEmpty(ApiKey);
        }

        return definition;
    }

    private IEnumerable<ModelDefinition> ModelDefinitions
        => AvailableModels.Where(m => !string.IsNullOrWhiteSpace(m.ModelId)).Select(m => m.ToModelDefinition(Vendor));

    private CliAgentDefinition BuildCli()
    {
        CliAgentDefinition cli = new(Vendor, DisplayName)
        {
            Id = Id,
            Platforms = Platforms,
            DefaultPrompt = DefaultPrompt,
            SvgIcon = Svg,
            WinSearchPaths = SplitLines(WinSearchPaths),
            MacSearchPaths = SplitLines(MacSearchPaths),
        };

        foreach (ModelDefinition model in ModelDefinitions) cli.Add(model);
        return cli;
    }

    private HttpAgentDefinition? BuildHttp(string? route)
    {
        string? trimmedHost = Host?.Trim();
        if (!Uri.TryCreate(trimmedHost, UriKind.Absolute, out _)) return null;

        HttpAgentDefinition http = new(Vendor, DisplayName)
        {
            Id = Id,
            Platforms = Platforms,
            DefaultPrompt = DefaultPrompt,
            SvgIcon = Svg,
            Host = trimmedHost,
            Protocol = Protocol,
            ModelsRoute = route,
            ApiKeyEnvVar = string.IsNullOrWhiteSpace(ApiKeyEnvVar) ? null : ApiKeyEnvVar.Trim(),
        };

        foreach (ModelDefinition model in ModelDefinitions) http.Add(model);
        return http;
    }

    private static List<string> SplitLines(string text)
        => [.. text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Icon? CreateIcon(string svg)
        => string.IsNullOrEmpty(svg) ? null : Rhino.UI.ImageResources.CreateEtoIcon(svg, 32, 32, HostUtils.RunningInDarkMode);

    private static void CopyPermissions(PermissionSet from, PermissionSet to)
    {
        to.DefaultPermission = from.DefaultPermission;
        foreach (Permission permission in from.AllowedTools()) to.AddPermission(permission);
        foreach (Permission permission in from.ProhibitedTools()) to.AddPermission(permission);
    }

}

internal sealed class UIModelDefinition : SimpleViewModel
{

    private string displayName = string.Empty;
    public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); }

    private string modelId = string.Empty;
    public string ModelId { get => modelId; set => SetProperty(ref modelId, value); }

    public UIModelDefinition()
    {
    }

    public UIModelDefinition(ModelDefinition definition)
    {
        displayName = definition.DisplayName;
        modelId = definition.ModelId;
    }

    public UIModelDefinition Duplicate() => new() { DisplayName = DisplayName, ModelId = ModelId };

    public ModelDefinition ToModelDefinition(string vendor)
        => new(vendor, string.IsNullOrWhiteSpace(DisplayName) ? ModelId.Trim() : DisplayName, ModelId.Trim());

}
