using RhinoCommand = Rhino.Commands.Command;

namespace Rhino.AI;

// Runs the ScriptEditor command when the editor is not open yet, which is what a command needs this
// style to be allowed to do.
[Rhino.Commands.CommandStyle(Rhino.Commands.Style.ScriptRunner)]
public class ScriptAssistantCommand : RhinoCommand
{
    public override string EnglishName => AIProfiles.Name(AIProfile.Script);

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
#if R9
        ScriptEditorRail.Toggle(doc.RuntimeSerialNumber);
        return Rhino.Commands.Result.Success;
#else
        return Rhino.Commands.Result.Nothing;
#endif
    }
}
