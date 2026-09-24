
namespace Rhino.AI;

/// <summary>
/// Many users don't need AI, or MCP, and spawning a small local http server "because" is bad practice.
/// </summary>
internal static class AIAutoLoad
{

    public static bool ShouldAutoLoad()
    {
        if (!AISettings.AutoLoadMCP) return false;
        foreach(AgentDefinition definition in AgentRegistry.Instance.AllDefinitions)
        {
            if (!definition.Available) continue;
            if (!AISettings.IsEnabled(definition)) continue;
            return true;
        }
        
        return false;
    }

}
