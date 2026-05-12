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

        if (RhinoMcpHost.HasStarted(doc))
        {
            if (!RhinoMcpHost.RestartOnPort(doc, port))
            {
                RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                return Result.Failure;
            }
            else
            {
                RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
            }
        }
        else if (RhinoMcpHost.Start(doc, port))
        {
            // Start runs WriteLine
        }
        else
        {
            RhinoApp.WriteLine($"[Rhino MCP] MCP server failed to start. Try a different port.");
        }

        return Result.Success;
    }
}
