using Eto.Forms;

namespace Rhino.AI.UI;

internal sealed class PermissibilityControl : Drawable
{
    
    public Permissability State { get; set; } = Permissability.Unset;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // TODO : Claude generate a 3 state sliding permission icon
    }

}

internal sealed class PermissionCell : CustomCell
{

    private IndirectBinding<Permissability> Bind { get; }

    protected override Control OnCreateCell(CellEventArgs args)
    {
        PermissibilityControl control = new ();
        control.BindDataContext(c => c.State, Bind);
        return control;
    };

    public PermissionCell(string argName)
    {
        Bind = Binding.Property<Permissability>(argName);
    }

    protected override void OnConfigureCell(CellEventArgs args, Control control)
    {
        control.DataContext = args.Item;
    }

}
