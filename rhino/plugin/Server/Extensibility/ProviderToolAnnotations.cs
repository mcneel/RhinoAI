namespace RhMcp.Server.Extensibility;

/// <summary>
/// The MCP tool annotations a contributing plug-in may declare. Both are advisory hints
/// passed on to the client, not anything this server enforces.
/// </summary>
internal sealed class ProviderToolAnnotations
{
    /// <summary>
    /// The tool does not modify state.
    /// </summary>
    public bool ReadOnlyHint { get; set; }

    /// <summary>
    /// The tool may make changes that are not easily undone.
    /// </summary>
    public bool DestructiveHint { get; set; }
}
