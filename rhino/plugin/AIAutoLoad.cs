
namespace Rhino.AI;

/// <summary>
/// Many users don't need AI, or MCP, and spawning a small local http server "because" is bad practice.
/// </summary>
internal static class AIAutoLoad
{

    private static bool WasStartedViaAgent { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MCPSpawnCommand.PortEnvVar));

    public static bool ShouldAutoLoad()
    {
        if (WasStartedViaAgent) return true;
        return AIInstances.Preferences.AutoLoadMCP && AgentHost.AnyUsable;
    }

}
