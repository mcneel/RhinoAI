using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp;

public class RhinoMcpCommand : Command
{

    public override string EnglishName => "RhinoMCP";

    private const int DefaultPort = 4862;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        int port = DefaultPort;

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
            gi.SetCommandPrompt("Port");
            gi.SetDefaultInteger(port);
            gi.SetLowerLimit(1, false);
            gi.SetUpperLimit(65535, false);
            if (gi.Get() != GetResult.Number) return Result.Cancel;

            port = gi.Number();
            if (!RhinoMcpHost.RestartOnPort(doc, port))
            {
                RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                return Result.Failure;
            }

            // Confirms a restart
            if (RhinoMcpHost.HasStarted(doc))
            {
                // TODO : Note old port down too
                RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
            }
        }

        if (!RhinoMcpHost.HasStarted(doc))
        {
            if (RhinoMcpHost.Start(doc, port))
            {
                // Start runs WriteLine
            }
            else
            {
                RhinoApp.WriteLine($"[Rhino MCP] MCP server failed to start. Try a different port.");
            }
        }

        return Result.Success;
    }
}
