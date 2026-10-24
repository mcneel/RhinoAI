using System.Collections.ObjectModel;

using Eto.Drawing;
using Eto.Forms;

using Rhino.AI.PlugIns;
using Rhino.Runtime;

namespace Rhino.AI.UI;

// Displays and allows for adding and deleting Agents
internal sealed class AIAgentDefinitionsPage : AIPage
{

    private AIAgentDefinitionsViewModel Model => (DataContext as AIAgentDefinitionsViewModel)!;

    public AIAgentDefinitionsPage()
    {
        DataContext = new AIAgentDefinitionsViewModel();
        InitLayout();
    }

    private void InitLayout()
    {
        Splitter splitter = new()
        {
            AllowDrop = false,
            Panel1 = GetDefinitionsView(),
            Panel2 = GetProperties(),
            FixedPanel = SplitterFixedPanel.Panel2,
        };

        Content = splitter;
    }

    private Control GetProperties()
    {
        DynamicLayout layout = new();
        layout.BeginVertical();

        // NOTE : Someone WILL say "Why aren't we using OptionsList here?" - It seems to be internal, so I can't

        Label displayName = new();
        displayName.BindDataContext(l => l.Text, Binding.Property((UIAgentDefinition d) => d.DisplayName).Cast<string?>());
        layout.Add(displayName);

        Label vendor = new();
        vendor.BindDataContext(l => l.Text, Binding.Property((UIAgentDefinition d) => d.Vendor).Cast<string?>());
        layout.Add(vendor);

        Label prompt = new();
        prompt.BindDataContext(l => l.Text, Binding.Property((UIAgentDefinition d) => d.DefaultPrompt).Cast<string?>());
        layout.Add(prompt);

        // TODO : Claude - Fill out the rest of this using the properties and bindings
        // For the models list, it should be a grid view with a +/- at the bottom left, right click context menu with delete/duplicate
        // Use the SegmentedButton
        // - should prompt user to confirm
        // + should just create a default UIAgent and add to the model

        layout.Add(new Rhino.UI.Controls.LabelSeparator());

        // Definition specific section

        DropDown typeDropDown = new()
        {
            DataStore = new ObservableCollection<object>(AIAgentDefinitionsViewModel.Types),
        };
        typeDropDown.BindDataContext(tdd => tdd.SelectedValue, Binding.Property((UIAgentDefinition d) => d.Type).Cast<object?>());
        layout.Add(typeDropDown);

        Panel customControls = new() { Padding = 0 };
        customControls.BindDataContext(cc => cc.Content, Binding.Property((UIAgentDefinition d) => d.Type).Convert(t =>
        {
            DynamicLayout layout = new();
            layout.BeginVertical();

            // TODO : Claude - Fill out the if statements using controls that match the inputs

            if (t == AgentType.Cli)
            {
                // List<string> WinSearchPaths
                // List<string> MacSearchPaths
            }
            else if (t == AgentType.Local)
            {
                // string? Host
                // ProtocolKind Protocol -> DropDown
                // string? ModelsRoute -> text box with good default / placeholder
            }
            else if (t == AgentType.Api)
            {
                // string? Host
                // public ProtocolKind Protocol
                // string? ApiKeyEnvVar
                // string? PrivateApiKey
                // public string? ApiKey
            }

            layout.EndVertical();

            return (Control?)layout;
        }));
        layout.Add(customControls);
        layout.Add(new Rhino.UI.Controls.LabelSeparator());

        layout.EndVertical();

        return layout;
    }

    private Control GetDefinitionsView()
    {
        GridView grid = new()
        {
            Enabled = false,
            AllowDrop = false,
            ShowHeader = false,
            Border = BorderType.Line,
            GridLines = GridLines.None,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            AllowMultipleSelection = false,
            CanDeleteItem = (_) => false,

            Columns =
            {
                new GridColumn()
                {
                    DataCell = new ImageViewCell(nameof(UIAgentDefinition.Icon))
                },
                new GridColumn()
                {
                    DataCell = new ImageViewCell(nameof(UIAgentDefinition.DisplayName))
                },
                new GridColumn()
                {
                    DataCell = new CheckBoxCell(nameof(UIAgentDefinition.Enabled))
                },
            }
        };

        SegmentedButton addRemove = new()
        {
            Items =
            {
                new ButtonSegmentedItem(Model.AddCommand) { Text = "Add" },
                new ButtonSegmentedItem(Model.RemoveCommand) { Text = "Remove" },
            },
        };

        DynamicLayout layout = new();
        layout.BeginVertical();
        layout.Add(new Label() { Text = "Agent Definitions" });
        layout.Add(grid);
        layout.AddSeparateColumn(addRemove, null);
        layout.EndVertical();

        return layout;
    }

    public override bool Validate() => true;

    public override bool TryApplyChanges()
    {
        bool result = true;
        foreach(UIAgentDefinition def in Model.Definitions)
        {
            Instances.Agents.Remove(def.Id);
            
            IAgentDefinition? newDef = def.GetDefinition();
            if (newDef is null)
            {
                result = false;
                continue;
            }
            
            result &= Instances.Agents.Add(newDef);
        }
        return result;
    }
    
}

internal sealed class AIAgentDefinitionsViewModel : SimpleViewModel
{

    public ObservableCollection<UIAgentDefinition> Definitions { get; }

    public int Index { get; set; } = 0;

    public UIAgentDefinition? Current => Definitions.ElementAtOrDefault(Index);

    public Command AddCommand { get; }
    public Command RemoveCommand { get; internal set; }

    public AIAgentDefinitionsViewModel()
    {
        Definitions = new (Instances.Agents.Definitions.Select(d => new UIAgentDefinition(d)));
        AddCommand = new Command(AddMethod);
        RemoveCommand = new Command(RemoveMethod);
    }

    private void RemoveMethod(object? _, EventArgs __)
    {
        if (Current is null) return;
        if (Index < 0) return;
        if (Index >= Definitions.Count) return;

        //  TODO : Prompt user first
        Definitions.RemoveAt(Index);
    }

    private void AddMethod(object? _, EventArgs __)
    {
        Definitions.Add(new UIAgentDefinition());
    }

    public bool TryAddNewDefinition(IAgentDefinition definition)
        => Instances.Agents.Add(definition);

    public static List<object> Types { get; } = [AgentType.Cli, AgentType.Api, AgentType.Local];

    public bool Validate()
    {
        // TODO : Validate Definitions.
        return false;
    }
    
}

internal sealed class UIAgentDefinition
{
    // TODO : Fill this out with good defaults for an API model
    public Guid Id { get; } = Guid.NewGuid();

    public string Vendor { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DefaultPrompt { get; set; } = string.Empty;

    public string Svg { get; set; } = string.Empty;

    public Icon? Icon { get; }

    public bool Available { get; } = false;

    public bool Enabled { get; set; }

    public string DefaultModelId { get; set; } = string.Empty;
    public string? CurrentModelId { get; set; }

    public Models.IModel? CurrentModel { get; }

    public PermissionSet Permissions { get; }

    public AgentType Type { get; }

    public HashSet<ModelDefinition> AvailableModels { get; } = [];

    public UIAgentDefinition()
    {
        
    }

    public UIAgentDefinition(IAgentDefinition definition)
    {
        // TODO : Claude fill out setters
        // TODO : Missing lots of properties from the http/cli definitions
        Type = definition switch
        {
            HttpAgentDefinition local when local.Host is not null => AgentType.Local,
            HttpAgentDefinition => AgentType.Api,
            CliAgentDefinition => AgentType.Cli,

            _ => AgentType.Unknown
        };

        if (!string.IsNullOrEmpty(Svg))
        {
            Icon = Rhino.UI.ImageResources.CreateEtoIcon(Svg, 32, 32, HostUtils.RunningInDarkMode);
        }
    }

    public IAgentDefinition? GetDefinition() => Type switch
    {
        // TODO : Claude fill out the constructors

        AgentType.Api => new HttpAgentDefinition(Vendor, DisplayName)
        {
            
        },
        AgentType.Local => new HttpAgentDefinition(Vendor, DisplayName)
        {
            
        },
        AgentType.Cli => new CliAgentDefinition(Vendor, DisplayName)
        {
            
        },

        _ => null
    };

}
