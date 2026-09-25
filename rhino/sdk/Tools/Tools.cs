using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rhino.AI;

/// <summary>A Tool inside of an MCP</summary>
public interface ITool
{

    /// <summary>1-128 characters, case-insensitive, only ASCII characters, No spaces, commas or other special characters besides _</summary>
    public string Name { get; }
    
    /// <summary>A description of the tool</summary>
    public string Description { get; }
    
    /// <summary>A readonly tool does not make any changes to anything it interacts with</summary>
    public bool ReadOnly { get; }

    /// <summary>A destructive tool can alter a file, potentially irrevocably</summary>
    public bool Destructive { get; }

    /// <summary>The available arguments for the <see cref="ITool"/></summary>
    public ToolParameter[] Args { get; }

    /// <summary>
    /// A call to run the tool
    /// </summary>
    /// <param name="args">The arguments for the tool call</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The <see cref="ToolReturn"/></returns>
    public Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token);

}


// Tool names (MCP spec 2025-11-25, Tools > Tool Names). All of these are SHOULD, not MUST:
// - 1 to 128 characters.
// - Case-sensitive.
// - Only ASCII letters, digits, _, - and ..
// - No spaces, commas or other special characters.
// - Unique within a server.

public abstract record Tool : ITool
{

    public string Name { get; }

    public string Description { get; }

    public bool ReadOnly { get; }

    public bool Destructive { get; }

    public ToolParameter[] Args { get; }
    
    public Tool(string name, string description, bool readOnly, bool destructive, ToolParameter[] args)
    {
        ValidateName(name);
        Name = name;
        Description = description;
        ReadOnly = readOnly;
        Destructive = destructive;
        Args = args;
    }

    /// <summary>
    /// (MCP spec 2025-11-25, Tools > Tool Names). All of these MUST be
    /// - 1 to 128 characters.
    /// - Only ASCII letters, digits, _, - (although I'm preventing - for simplicity)
    /// - No spaces, commas or other special characters.
    /// - Case sensitive (although for simplicity, this SDK is case insensitive)
    /// </summary>
    internal static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Length > 128) throw new ArgumentException($"Tool name must be at most 128 characters, got {name.Length}", nameof(name));
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]+$")) throw new ArgumentException($"Tool name '{name}' may only contain ASCII letters, digits and _", nameof(name));
    }


    public abstract Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token);

}

/// <summary>
/// A parameter 
/// </summary>
/// <param name="Name">The name of the tool. Please ensure the name uses between 1-128 characters, only ASCII characters, No spaces, commas or other special characters besides _</param>
/// <param name="Description">A description of the tool type</param>
/// <param name="Type">The argument type</param>
/// <param name="Required">Required parameters will be </param>
public record struct ToolParameter
{
    public string Name { get; }
    public string Description { get; }
    public ToolArgType Type { get; }
    public bool Required { get;  }
    

    public ToolParameter(string name, string description, ToolArgType type, bool required)
    {
        Tool.ValidateName(name);
        Name = name;
        Description = description;
        Type = type;
        Required = required;
    }
    
}
