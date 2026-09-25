using System;

namespace Rhino.AI.PlugIns;

public sealed record PlugInToken
{

    public Guid Id { get; }

    public string Name { get; }

    internal PlugInToken(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public static PlugInToken Invalid { get; } = new (Guid.Empty, "Invalid");

    public bool IsInvalud() => Id == Guid.Empty;

}
