using Rhino.Commands;

namespace RhMcp;

// Hidden autostart entry point invoked by the router via `-runscript=_-StartMCP _Enter`.
// The port arrives via the RHINO_MCP_AUTOSTART_PORT env var so we don't have to feed
// arguments through the runscript engine — that path is racy with plugin load timing.
[CommandStyle(Style.ScriptRunner | Style.Hidden)]
public class StartMCPCommand : Command
{

    public const string PortEnvVar = "RHINO_MCP_AUTOSTART_PORT";

    public override string EnglishName => "StartMCP";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        string? portStr = Environment.GetEnvironmentVariable(PortEnvVar);
        if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
        {
            RhinoApp.WriteLine($"[Rhino MCP] StartMCP: {PortEnvVar} not set or invalid (got '{portStr}').");
            return Result.Failure;
        }
        
        // Obfuscate the StartMCP command a little.
        RhinoApp.ClearCommandHistoryWindow();

        return RhinoMcpHost.StartOrRestart(doc, port) ? Result.Success : Result.Failure;
    }

}
