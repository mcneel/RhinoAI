using System.Collections.ObjectModel;

using Eto.Forms;

namespace Rhino.AI.UI;

internal sealed class AIPermissionsPage : AIPage
{

    public AIPermissionsViewModel Model => (DataContext as AIPermissionsViewModel)!;
    
    // TODO : What level of permissions do we have?
    //        A user WOULD want some global settings

    // TODO : This layout is kinda crappy honestly.

    public GridView Global { get; } = new ()
    {
        Columns =
        {
            new GridColumn()
            {
                DataCell = new TextBoxCell(nameof(Permission.McpName))
            },
            new GridColumn()
            {
                DataCell = new TextBoxCell(nameof(Permission.ToolName))
            },
            new GridColumn()
            {
                DataCell = new PermissionCell(nameof(Permission.Permissability))
            }
        }
    };
    
    public GridView Agents { get; } = new ()
    {
        Columns =
        {
            new GridColumn()
            {
                DataCell = new PermissionCell(nameof(Permission.Permissability))
            }
        }
    };

    public AIPermissionsPage()
    {
        DataContext = new AIPermissionsViewModel();
        InitLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Model.Update();
    }

    private void InitLayout()
    {
        DynamicLayout layout = new();
        layout.BeginVertical();
        
        layout.Add(new Label() { Text = "Global Permissions" });
        layout.Add(Global);

        layout.Add(new Label() { Text = "Agent Permissions" });
        layout.Add(Agents);

        layout.EndVertical();

        Content = layout;
    }

    public override bool Validate() => true;

    public override bool TryApplyChanges()
        => Model.TryApplyChanges();

    // Global Permissions

    // Agent Definition permissions

}

internal sealed class AIPermissionsViewModel : SimpleViewModel
{

    public UIPermission GlobalPermissions { get; private set; } = new(AIInstances.Preferences.Permissions);

    public ObservableCollection<UIPermission> AgentPermissions { get; } = [];

    public AIPermissionsViewModel()
    {
        Update();
    }

    public void Update()
    {
        GlobalPermissions = new UIPermission(AIInstances.Preferences.Permissions);
        RaisePropertyChanged(nameof(GlobalPermissions));

        AgentPermissions.Clear();
        foreach (IAgentDefinition definition in AIInstances.Agents.Definitions)
        {
            AgentPermissions.Add(new UIPermission(definition));
        }
    }

    public record UIPermission
    {
        // TODO : Fill out properties

        public IAgentDefinition? Definition { get; }

        public UIPermission(PermissionSet set)
        {
            // TODO : Set properties
        }

        public UIPermission(IAgentDefinition definition) : this(definition.Permissions)
        {
            Definition = definition;
        }

        public bool TryApply()
        {
            if (Definition is null)
            {
                return Push(AIInstances.Preferences.Permissions);
            }
            
            return Push(Definition.Permissions);
        }

        private bool Push(PermissionSet permissions)
        {
            // TODO : Clear permissions first?
            // permissions.AddPermission();
            return false;
        }
        
    }

    internal bool TryApplyChanges()
    {
        bool result = GlobalPermissions.TryApply();
        foreach(UIPermission permission in AgentPermissions)
        {
            result &= permission.TryApply();
        }

        return result;
    }

    // TODO : How to put back?

}
