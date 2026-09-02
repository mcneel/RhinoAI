using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhinoAI;

public class MCPStatusCommand : Command
{
    public override string EnglishName => "McpStatus";

    protected override string CommandContextHelpUrl => DocsLinks.Homepage;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (RhinoAIHost.TryGetPortFor(doc, out int port))
        {
            RhinoApp.WriteLine($"[Rhino MCP] MCP server running on http://localhost:{port}/");
            return Result.Success;
        }

        RhinoApp.WriteLine("[Rhino MCP] No MCP server running for this document.");

        GetOption go = new();
        go.SetCommandPrompt("Would you like to start one?");
        go.AddOption("Yes");
        int noIndex = go.AddOption("No");
        if (go.Get() is not GetResult.Option) return Result.Cancel;
        if (go.Option().Index == noIndex) return Result.Nothing;

        if (!RhinoAIHost.TryGetNextPort(out int nextPort))
        {
            RhinoApp.WriteLine("[Rhino MCP] Failed to start: no free port available.");
            return Result.Failure;
        }

        return RhinoAIHost.StartOrRestart(doc, nextPort) ? Result.Success : Result.Failure;
    }
}
