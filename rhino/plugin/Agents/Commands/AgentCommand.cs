using Rhino.Commands;
using Rhino.Input.Custom;

namespace RhMcp;

public abstract class AgentCommand : Command
{
    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    private protected abstract string AgentName { get; }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // NOTE : On Rhino 8 Mac Get Literal String doesn't work so idk
        GetString get = new();
        get.SetCommandPrompt(EnglishName);

        if (get.GetLiteralString() != Rhino.Input.GetResult.String) return Result.Cancel;
        string request = get.StringResult();
        if (string.IsNullOrWhiteSpace(request)) return Result.Cancel;

        AgentHost.SetActive(doc, AgentName);
        AgentDispatch.PromptActive(doc, UserMessage.FromText(request));
        return Result.Success;
    }
}
