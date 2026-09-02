using System.Diagnostics;
using RhinoCommand = Rhino.Commands.Command;

namespace RhinoAI;

public class MCPHelpCommand : RhinoCommand
{
    public override string EnglishName => "MCPHelp";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        Process.Start(new ProcessStartInfo(CommandContextHelpUrl) { UseShellExecute = true });
        return Rhino.Commands.Result.Success;
    }
}
