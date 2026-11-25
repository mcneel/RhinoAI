using Rhino.Commands;

namespace Rhino.AI;

// Hidden autostart entry point invoked by the router via `-runscript=_-MCPSpawn _Enter`.
// The port arrives via the RHINO_MCP_AUTOSTART_PORT env var so we don't have to feed
// arguments through the runscript engine: that path is racy with plugin load timing.
[CommandStyle(Style.ScriptRunner | Style.Hidden)]
public class MCPSpawnCommand : Command
{

    public const string PortEnvVar = "RHINO_MCP_AUTOSTART_PORT";

    public override string EnglishName => "MCPSpawn";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        string? portStr = Environment.GetEnvironmentVariable(PortEnvVar);
        if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
        {
            RhinoApp.WriteLine($"[RhinoAI] MCPSpawn: {PortEnvVar} not set or invalid (got '{portStr}').");
            return Result.Failure;
        }

        try
        {
            // The NewDocument auto-start may already have bound this port, and the
            // router may already be sending to it: restarting would drop that request.
            if (RhinoAIHost.TryGetPortFor(doc, out int existing) && existing == port)
                return Result.Success;
            return RhinoAIHost.StartOrRestart(doc, port) ? Result.Success : Result.Failure;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[RhinoAI] MCPSpawn threw: {ex}");
            return Result.Failure;
        }
    }

}
