using RhinoCommand = Rhino.Commands.Command;

namespace RhinoAI;

public class WebAIPanelCommand : RhinoCommand
{
    public override string EnglishName => "AIPanelWeb";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        Guid panelId = WebAIPanel.PanelId;
        if (Rhino.UI.Panels.IsPanelVisible(panelId))
            Rhino.UI.Panels.ClosePanel(panelId);
        else
            Rhino.UI.Panels.OpenPanel(panelId);
        return Rhino.Commands.Result.Success;
    }
}
