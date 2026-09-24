namespace Rhino.AI;

public enum ToolArgType { Unknown = 0, String, URL, FilePath, Number, Integer, Boolean, Array, Object };

public interface IToolArg { public string Name { get; } };

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

public sealed record ToolBoolean(string Name, bool Value) : ToolArg(Name) { }

public sealed record ToolInt(string Name, int Value) : ToolArg(Name) { }

public sealed record ToolPath(string Name, string Value) : ToolArg(Name) { }

public sealed record ToolUrl(string Name, string Value) : ToolArg(Name) { }

public sealed record ToolString(string Name, string Value) : ToolArg(Name) { }

public sealed record ToolSecret(string Name, string Value) : ToolArg(Name) { }
