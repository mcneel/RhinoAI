namespace Rhino.AI;

/// <summary>
/// Argument Type
/// </summary>
public enum ToolArgType { Unknown = 0, String, URL, FilePath, Number, Integer, Boolean, Array, Object };

/// <summary>
/// An input Argument for Tool calls
/// </summary>
public interface IToolArg
{
    
    /// <summary>
    /// The name of the <see cref="IToolArg"/>
    /// </summary>
    public string Name { get; }

};

/// <summary>
/// An input Argument for Tool calls
/// </summary>
public abstract record ToolArg : IToolArg
{
    
    public string Name { get; }

    private ToolArg()
    {
        Name = "Error";
    }

    public ToolArg(string name)
    {
        Name = name;
        Tool.ValidateName(name);
    }
}

/// <summary>
/// A <see cref="bool"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolBoolean(string Name, bool Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="double"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public record struct ToolNumber(string Name, double Value) : IToolArg;

/// <summary>
/// A <see cref="int"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolInt(string Name, int Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolPath(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolUrl(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolString(string Name, string Value) : ToolArg(Name) { }

/// <summary>
/// A <see cref="string"/> argument for a tool call
/// </summary>
/// <param name="Name">The tool argument name</param>
/// <param name="Value">The tool argument's value</param>
public sealed record ToolSecret(string Name, string Value) : ToolArg(Name) { }
