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
        // Instances.Preferences.UseSdk = UseSdkSetting;
    }

    protected override Commands.Result RunCommand(RhinoDoc doc, Commands.RunMode mode)
    {
        // AI : Toggle Instances.Preferences.UseSdk like any other Test Command
        
        return Commands.Result.Success;
    }
}
