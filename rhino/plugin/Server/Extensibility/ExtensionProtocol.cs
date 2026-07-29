using System.Text.RegularExpressions;

namespace RhMcp.Server.Extensibility;

// The contract other Rhino plug-ins use to contribute MCP tools to this server.
// See McpExtensionHost for the entry point and docs/EXTENSIBILITY.md for the
// provider-facing spec.
//
// This file is pure -- no Rhino types, no reflection -- so it is compiled into
// tests/Server.Tests and unit-tested there.
internal static class ExtensionProtocol
{
    // Bumped only for a breaking change to McpExtensionHost's members or to the
    // descriptor shape. Exposed to callers as McpExtensionHost.McpExtensionProtocol.
    public const int Version = 1;

    // Reserved for the host's own gateway tools (ext_list_tools / ext_call_tool);
    // a contributing plug-in may not register into it.
    public const string ReservedToolPrefix = "ext";

    private static readonly Regex ToolNamePattern =
        new("^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidToolName(string? name) =>
        !string.IsNullOrEmpty(name) && ToolNamePattern.IsMatch(name);

    public static bool IsReservedName(string name) =>
        name.StartsWith(ReservedToolPrefix + "_", StringComparison.OrdinalIgnoreCase);
}

// One tool as declared by a contributing plug-in.
internal sealed class ProviderToolDescriptor
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string Description { get; set; } = "";
    public JsonElement InputSchema { get; set; }
    public bool ReadOnly { get; set; }
    public bool Destructive { get; set; }

    // Opt IN to UI-thread marshalling. The default is background, the inverse of the
    // compiled-tool default: a contributing plug-in does its own marshalling and its
    // work may run for minutes, and holding the Rhino message pump for that is a
    // frozen application.
    public bool RequiresUiThread { get; set; }

    // Rejection is by message rather than exception: a bad descriptor is the
    // registering plug-in's problem to report, not grounds for taking down a call
    // that is happening inside someone else's plug-in load path.
    public static bool TryParse(string? json, out ProviderToolDescriptor descriptor, out string failure)
    {
        descriptor = new ProviderToolDescriptor();
        failure = "";

        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "descriptor is empty";
            return false;
        }

        JsonElement root;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json!);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            failure = $"descriptor is not valid JSON: {ex.Message}";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            failure = "descriptor must be a JSON object";
            return false;
        }

        descriptor.Owner = ReadString(root, "owner") ?? "";
        if (string.IsNullOrWhiteSpace(descriptor.Owner))
        {
            failure = "descriptor is missing a non-empty \"owner\"";
            return false;
        }

        string? name = ReadString(root, "name");
        if (!ExtensionProtocol.IsValidToolName(name))
        {
            failure = $"\"name\" '{name ?? "(missing)"}' is not a valid tool name";
            return false;
        }
        descriptor.Name = name!;

        string? description = ReadString(root, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            // Not pedantry: an LLM cannot decide to call a tool it has no description
            // for, so an undescribed tool is dead weight in every client's context.
            failure = $"'{descriptor.Name}' has no \"description\"";
            return false;
        }
        descriptor.Description = description!;

        if (!root.TryGetProperty("inputSchema", out JsonElement schema) || schema.ValueKind != JsonValueKind.Object)
        {
            failure = $"'{descriptor.Name}' is missing an \"inputSchema\" object";
            return false;
        }
        descriptor.InputSchema = schema.Clone();

        descriptor.Title = ReadString(root, "title");

        if (root.TryGetProperty("annotations", out JsonElement annotations) && annotations.ValueKind == JsonValueKind.Object)
        {
            descriptor.ReadOnly = ReadBool(annotations, "readOnlyHint");
            descriptor.Destructive = ReadBool(annotations, "destructiveHint");
        }

        descriptor.RequiresUiThread = ReadBool(root, "requiresUiThread");
        return true;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
