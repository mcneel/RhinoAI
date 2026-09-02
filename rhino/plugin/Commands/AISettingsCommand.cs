using RhinoCommand = Rhino.Commands.Command;

namespace RhinoAI;

public class MCPSettingsCommand : RhinoCommand
{
    public override string EnglishName => "AISettings";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        AISettingsDialog dialog = new();
        dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        return Rhino.Commands.Result.Success;
    }
}
