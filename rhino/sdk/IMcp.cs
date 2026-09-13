using System.Collections.Generic;

namespace Rhino.AI;

public sealed record Tool(string Name, string Description, bool ReadOnly, bool Destructive, params ToolArg[] Args)
{

    // public string UseTool(IEnumerable<(ToolArg Arg, string Value)> Args)
    // {
        
    // }

}

public record struct ToolArg(string Name, string Description);

public interface IMcp
{

    public string Name { get; }

    public MCPConnectionType ConnectionType { get; }

    public IReadOnlyDictionary<string, Tool> Tools { get; }

    // public Task<bool> ConnectAsync()

}

public enum MCPConnectionType { HTTP, STDIO };
