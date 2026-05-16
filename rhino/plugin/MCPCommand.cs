using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

public class MCPCommand : Command
{

    public override string EnglishName => "RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        int port = RhinoMcpHost.GetNextPort();

        GetInteger go = new ();
        go.SetCommandPrompt("RhinoMCP Port");
        go.AcceptNothing(true);
        go.AcceptEnterWhenDone(true);
        go.SetDefaultInteger(port);
        go.SetLowerLimit(1, false);
        go.SetUpperLimit(65535, false);
        if (go.Get() != GetResult.Number) return Result.Cancel;
        port = go.Number();

        return RhinoMcpHost.StartOrRestart(doc, port) ? Result.Success : Result.Failure;
    }
}
