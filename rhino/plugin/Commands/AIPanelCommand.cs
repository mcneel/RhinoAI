using RhinoCommand = Rhino.Commands.Command;

namespace RhinoAI;

public class AIPanelCommand : RhinoCommand
{
    public override string EnglishName => "AIPanel";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        Guid panelId = AIPAnel.PanelId;
        bool visible = Rhino.UI.Panels.IsPanelVisible(panelId);
        if (visible)
            Rhino.UI.Panels.ClosePanel(panelId);
        else
            Rhino.UI.Panels.OpenPanel(panelId);
        return Rhino.Commands.Result.Success;
    }
}
