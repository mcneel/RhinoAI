using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

public class RhinoMcpCommand : Command
{
    public override string EnglishName => "RhinoMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine($"[Rhino MCP] MCP server currently running on http://localhost:{RhMcpHost.Port}/");
        if (RhMcpHost.HasStarted) return Result.Success;

        var go = new GetOption();
        go.SetCommandPrompt("RhinoMCP");
        go.AcceptNothing(true);
        var setPortOpt = go.AddOption("SetPort");

        var res = go.Get();
        if (res == GetResult.Nothing) return Result.Success;
        if (res != GetResult.Option) return Result.Cancel;

        if (go.Option().Index == setPortOpt)
        {
            var gi = new GetInteger();
            gi.SetCommandPrompt("New port");
            gi.SetDefaultInteger(RhMcpHost.Port);
            gi.SetLowerLimit(1, false);
            gi.SetUpperLimit(65535, false);
            if (gi.Get() != GetResult.Number) return Result.Cancel;

            var port = gi.Number();
            if (!RhMcpHost.RestartOnPort(port))
            {
                RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                return Result.Failure;
            }
            RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
        }

        return Result.Success;
    }
}
