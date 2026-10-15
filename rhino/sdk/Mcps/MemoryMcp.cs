namespace Rhino.AI;

/// <summary>
/// A MCP that lives entirely in Memory.
/// </summary>
/// <param name="name">The name of the MCP</param>
public class MemoryMcp(string name) : GenericMcp(name)
{
    
    internal void ClearTools() => PrivateTools.Clear();

}
