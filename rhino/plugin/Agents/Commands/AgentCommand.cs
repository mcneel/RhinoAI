using Rhino.Commands;
using Rhino.Input.Custom;

namespace Rhino.AI;

[Rhino.Commands.CommandStyle(Rhino.Commands.Style.Hidden)]
public abstract class AgentCommand : Command
{
    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    private protected abstract string Vendor { get; }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (!RhinoApp.IsInternetAccessAllowed)
        {
            RhinoApp.WriteLine("Internet Access is set to do not allow.");
            return Result.Cancel;
        }

        // NOTE : On Rhino 8 Mac Get Literal String doesn't work so idk
        GetString get = new();
        get.SetCommandPrompt(EnglishName);

        if (get.GetLiteralString() != Rhino.Input.GetResult.String) return Result.Cancel;
        string request = get.StringResult();
        if (string.IsNullOrWhiteSpace(request)) return Result.Cancel;

        if (AIInstances.Agents.FromParameters(Vendor, AgentType.Cli) is not { } definition)
        {
            RhinoApp.WriteLine($"{EnglishName} is not configured. Open AI Settings to set it up.");
            return Result.Cancel;
        }

        AgentHost.SetActive(doc, definition.Id);
        AgentDispatch.PromptActive(doc, UserMessage.FromText(request));
        return Result.Success;
    }
}
