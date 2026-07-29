using System.Text.Json.Serialization;

namespace RhMcp.Server.Extensibility;

// One tool as declared by a contributing plug-in, and the parsing of the descriptor
// JSON it arrives as. See McpExtensionHost.RegisterMcpTool for the shape.
//
// Deserialization is System.Text.Json with McpSerializer.Options, the same as every
// other request DTO in this folder; only the validation below is hand-written, since
// "is this usable, and which field is wrong" is not something a serializer answers.
//
// Pure -- no Rhino types, no reflection -- so it can be compiled into
// tests/Server.Tests and unit-tested there without a running Rhino.
internal sealed class ProviderToolDescriptor
{
    /// <summary>
    /// The reverse-DNS identifier of the plug-in contributing this tool, e.g.
    /// <c>com.example.myplugin</c>. Required.
    /// </summary>
    /// <remarks>
    /// Groups a plug-in's tools so they can be removed together, and decides whether a
    /// repeat registration of the same <see cref="Name"/> is an update by the same owner
    /// or a collision between two different plug-ins.
    /// </remarks>
    public string Owner { get; set; } = "";

    /// <summary>
    /// The tool name an MCP client calls, unique across the whole server. Required.
    /// </summary>
    /// <remarks>
    /// Validated against <see cref="ExtensionProtocol.IsValidToolName"/>. Names beginning
    /// <c>ext_</c> are reserved for the host's own tools and are refused at registration.
    /// </remarks>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional human-readable display name. Falls back to <see cref="Name"/> in clients
    /// that show one.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// What the tool does, in prose, for the model rather than for a developer. Required.
    /// </summary>
    /// <remarks>
    /// Not pedantry: an LLM cannot decide to call a tool it has no description for, so an
    /// undescribed tool is dead weight in every client's context window.
    /// </remarks>
    public string Description { get; set; } = "";

    /// <summary>
    /// JSON Schema for the tool's arguments, as an object with <c>"type": "object"</c>.
    /// Required, and passed through to clients verbatim.
    /// </summary>
    /// <remarks>
    /// Safe to hold indefinitely: a <see cref="JsonElement"/> materialised by the
    /// deserializer owns its own backing document rather than borrowing the one being
    /// read, so it stays valid long after the parse returns.
    /// </remarks>
    public JsonElement InputSchema { get; set; }

    /// <summary>
    /// The MCP tool annotations, if the descriptor declared any.
    /// </summary>
    public ProviderToolAnnotations? Annotations { get; set; }

    /// <summary>
    /// Opt IN to running the handler on the Rhino UI thread. Defaults to false.
    /// </summary>
    /// <remarks>
    /// The default is the inverse of the one applied to tools compiled into this plug-in.
    /// A contributing plug-in does its own marshalling and knows what it touches, and its
    /// work may run for minutes; holding the Rhino message pump for that is a frozen
    /// application. Opting in covers only the synchronous prefix of an async handler.
    /// </remarks>
    public bool RequiresUiThread { get; set; }

    /// <summary>
    /// The MCP <c>readOnlyHint</c> annotation: the tool does not modify state. Advisory,
    /// and false when unstated.
    /// </summary>
    [JsonIgnore]
    public bool ReadOnly => Annotations?.ReadOnlyHint ?? false;

    /// <summary>
    /// The MCP <c>destructiveHint</c> annotation: the tool may make changes that are not
    /// easily undone. Advisory, and false when unstated.
    /// </summary>
    [JsonIgnore]
    public bool Destructive => Annotations?.DestructiveHint ?? false;

    /// <summary>
    /// Parses and validates a descriptor as supplied to
    /// <see cref="McpExtensionHost.RegisterMcpTool"/>.
    /// </summary>
    /// <param name="json">The descriptor JSON. Null, blank, non-JSON and non-object all fail.</param>
    /// <param name="descriptor">The parsed descriptor. Never null, but only meaningful when this returns true.</param>
    /// <param name="failure">Empty on success, otherwise why the descriptor was rejected, naming the offending field.</param>
    /// <returns>True when the descriptor is usable.</returns>
    /// <remarks>
    /// Rejection is by message rather than exception: a bad descriptor is the registering
    /// plug-in's problem to report, not grounds for throwing inside someone else's plug-in
    /// load path. Validation is all-or-nothing -- a descriptor describes one tool, so there
    /// is nothing to salvage from a bad one.
    /// </remarks>
    public static bool TryParse(string? json, out ProviderToolDescriptor descriptor, out string failure)
    {
        descriptor = new ProviderToolDescriptor();
        failure = "";

        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "descriptor is empty";
            return false;
        }

        try
        {
            ProviderToolDescriptor? parsed =
                JsonSerializer.Deserialize<ProviderToolDescriptor>(json!, McpSerializer.Options);

            if (parsed is null)
            {
                failure = "descriptor must be a JSON object";
                return false;
            }

            descriptor = parsed;
        }
        catch (JsonException ex)
        {
            // Covers malformed JSON, a root that is not an object, and a field of the
            // wrong type -- all of which are the registering plug-in's bug to fix.
            failure = $"descriptor could not be read: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.Owner))
        {
            failure = "descriptor is missing a non-empty \"owner\"";
            return false;
        }

        if (!ExtensionProtocol.IsValidToolName(descriptor.Name))
        {
            failure = $"\"name\" '{(string.IsNullOrEmpty(descriptor.Name) ? "(missing)" : descriptor.Name)}' "
                      + "is not a valid tool name";
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.Description))
        {
            failure = $"'{descriptor.Name}' has no \"description\"";
            return false;
        }

        if (descriptor.InputSchema.ValueKind != JsonValueKind.Object)
        {
            failure = $"'{descriptor.Name}' is missing an \"inputSchema\" object";
            return false;
        }

        return true;
    }
}
