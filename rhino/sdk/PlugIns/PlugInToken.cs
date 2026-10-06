using System;

namespace Rhino.AI.PlugIns;

/// <summary>
/// A Permissions Token for a PlugIn
/// </summary>
public sealed record PlugInToken
{

    /// <summary>
    /// The PlugIn Id.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The Name of the PlugIn
    /// </summary>
    public string Name { get; }

    internal PlugInToken(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>
    /// An Invalid token, this token is always invalid. Use only for Unit Tests.
    /// </summary>
    public static PlugInToken Invalid { get; } = new (Guid.Empty, "Invalid");

    /// <summary>
    /// Checks a <see cref="PlugInToken"/> for validity
    /// </summary>
    /// <returns>True if the PlugIn is valid</returns>
    public bool IsInValid() => Id == Guid.Empty;

}
