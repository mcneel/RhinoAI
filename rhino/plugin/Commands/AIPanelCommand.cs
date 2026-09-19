using RhinoCommand = Rhino.Commands.Command;

namespace Rhino.AI;

public class AIPanelCommand : RhinoCommand
{
    public override string EnglishName => "AIPanel";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Commands.Result RunCommand(RhinoDoc doc, Commands.RunMode mode)
    {
        bool visible = Rhino.UI.Panels.IsPanelVisible(UI.AIPanel.PanelId);
        if (visible)
            Rhino.UI.Panels.ClosePanel(UI.AIPanel.PanelId);
        else
            Rhino.UI.Panels.OpenPanel(UI.AIPanel.PanelId);
        
        return Commands.Result.Success;
    }
}
