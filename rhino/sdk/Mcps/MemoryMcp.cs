using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

/// <summary>
/// A MCP that lives entirely in Memory.
/// </summary>
/// <param name="name">The name of the MCP</param>
public sealed class MemoryMcp(string name) : Mcp(name)
{
    
    internal void ClearTools() => PrivateTools.Clear();

    public override Task<bool> InitAsync(CancellationToken token)
    {
        // TODO : Init, use the lease thing maybe?
        return base.InitAsync(token);
    }

}
