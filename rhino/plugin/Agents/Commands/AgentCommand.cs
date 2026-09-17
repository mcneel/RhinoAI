using Rhino.Commands;
using Rhino.Input.Custom;

namespace Rhino.AI;

// Prompting an agent from the command line is a development surface; the AI and Assistant panels are
// the shipping one. So these are test commands: their names start with "Test", and Style.Hidden is
// what actually makes Rhino treat a managed command as one (it maps to the test-command flag), which
// keeps them out of autocomplete while a user who knows the name can still type it.
[CommandStyle(Style.Hidden)]
public abstract class AgentCommand : Command
{
    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    private protected abstract string AgentName { get; }

    // What the command line calls the agent. The command name carries a "Test" prefix that exists
    // only to keep it out of autocomplete, so prompting with it would read as the wrong thing; this
    // comes from the agent's own definition, which also keeps a renamed or custom agent correct.
    private protected virtual string DisplayName =>
        AgentRegistry.Instance.TryGet(AgentName, out AgentDefinition def) ? def.Name : AgentName;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // NOTE : On Rhino 8 Mac Get Literal String doesn't work so idk
        GetString get = new();
        get.SetCommandPrompt(DisplayName);

        if (get.GetLiteralString() != Rhino.Input.GetResult.String) return Result.Cancel;
        string request = get.StringResult();
        if (string.IsNullOrWhiteSpace(request)) return Result.Cancel;

        AgentHost.SetActive(doc, AIProfile.Rhino, AgentName);
        AgentDispatch.PromptActive(doc, AIProfile.Rhino, UserMessage.FromText(request));
        return Result.Success;
    }
}
