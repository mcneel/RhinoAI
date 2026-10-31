using RhinoCommand = Rhino.Commands.Command;
using Rhino.UI;

namespace Rhino.AI;

[Commands.CommandStyle(Commands.Style.Hidden)]
public class TestAgenticCommand : RhinoCommand
{
    public override string EnglishName => "TestAgentic";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    public TestAgenticCommand()
    {
        
    }

    protected override Commands.Result RunCommand(RhinoDoc doc, Commands.RunMode mode)
    {
        Instances.Preferences.UseSdk = !Instances.Preferences.UseSdk;
        RhinoApp.WriteLine("UseSdk is now {0}", Instances.Preferences.UseSdk ? "on" : "off");
        return Commands.Result.Success;
    }
}
