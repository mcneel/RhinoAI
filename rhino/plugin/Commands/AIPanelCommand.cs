using RhinoCommand = Rhino.Commands.Command;
using Rhino.UI;

namespace Rhino.AI;

public class AIPanelCommand : RhinoCommand
{
    public override string EnglishName => "AI";

    public override string LocalName => LOC.COMMANDNAME(EnglishName);

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
