using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

public class MCPStartCommand : Command
{

    public override string EnglishName => "MCPStart";

    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        GetInteger go = new ();
        go.SetCommandPrompt("MCPStart Port");
        go.AcceptNothing(true);
        go.AcceptEnterWhenDone(true);
        bool hasDefault = RhinoMcpHost.TryGetNextPort(out int suggestedPort);
        if (hasDefault)
            go.SetDefaultInteger(suggestedPort);
        go.SetLowerLimit(1, false);
        go.SetUpperLimit(65535, false);
        GetResult res = go.Get();
        // Nothing means Enter pressed; only valid when a default was actually set, otherwise go.Number() yields 0.
        if (res is GetResult.Nothing && !hasDefault) return Result.Cancel;
        if (res is not (GetResult.Number or GetResult.Nothing)) return Result.Cancel;
        int port = go.Number();

        return RhinoMcpHost.StartOrRestart(doc, port) ? Result.Success : Result.Failure;
    }
}
