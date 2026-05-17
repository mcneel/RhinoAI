using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RhMcp.Server;

// Builds a JSON Schema (draft-2020-12 flavour, the minimal subset MCP cares
// about) from a method's parameter list. Skips parameters supplied by the
// host (IServiceProvider-resolved + CancellationToken) since those never
// show up in the wire-level arguments object.
//
// Type mapping is intentionally shallow: primitive types get specific schema
// "type" values; complex types fall back to "object" with no inner shape, on
// the theory that LLMs cope better with a loose schema than with an aggressive
// one that lies about reality. Add nested-object inspection here when a tool
// actually benefits.
internal static class SchemaBuilder
{
    public static JsonElement BuildInputSchema(IReadOnlyList<ParameterDescriptor> descriptors)
    {
        JsonObject properties = new();
        JsonArray required = new();

        foreach (ParameterDescriptor p in descriptors)
        {
            if (!p.IncludeInSchema) continue;

            JsonObject prop = new() { ["type"] = MapType(p.ParameterType) };
            if (!string.IsNullOrEmpty(p.Description))
                prop["description"] = p.Description;
            properties[p.WireName] = prop;

            if (p.IsRequired) required.Add(p.WireName);
        }

        JsonObject schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0) schema["required"] = required;

        // Serializer round-trip lets us hand back a JsonElement (the type MCP
        // protocol DTOs expect) without depending on JsonNode.Deserialize<JsonElement>
        // overloads that don't exist on STJ 8 in some shapes.
        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString(McpSerializer.Options));
    }

    private static JsonNode MapType(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;

        return u switch
        {
            _ when u == typeof(string) || u == typeof(Guid) || u == typeof(Uri) ||
                   u == typeof(DateTime) || u == typeof(DateTimeOffset) || u == typeof(TimeSpan) => "string",
            _ when u == typeof(bool) => "boolean",
            _ when u == typeof(byte) || u == typeof(sbyte) || u == typeof(short) || u == typeof(ushort) ||
                   u == typeof(int) || u == typeof(uint) || u == typeof(long) || u == typeof(ulong) => "integer",
            _ when u == typeof(float) || u == typeof(double) || u == typeof(decimal) => "number",
            { IsEnum: true } => "string",
            { IsArray: true } => "array",
            _ when IsCollectionType(u) => "array",
            _ => "object",
        };
    }

    private static bool IsCollectionType(Type u)
    {
        if (!u.IsGenericType) return false;
        Type def = u.GetGenericTypeDefinition();
        return def == typeof(List<>) ||
               def == typeof(IEnumerable<>) ||
               def == typeof(IReadOnlyList<>) ||
               def == typeof(IReadOnlyCollection<>) ||
               def == typeof(ICollection<>);
    }
}

// Describes a single parameter after binding-strategy resolution. ToolHandler
// and ResourceHandler build these up at registration time so invocation is
// just a walk over an array.
internal sealed class ParameterDescriptor
{
    public ParameterInfo Parameter { get; }
    public string WireName { get; }
    public string? Description { get; }
    public ParameterBindingKind Kind { get; }
    public object? ServiceKey { get; }
    public Type ParameterType => Parameter.ParameterType;
    public bool IncludeInSchema => Kind == ParameterBindingKind.Argument;
    public bool IsRequired
    {
        get
        {
            if (Parameter.HasDefaultValue) return false;
            Type pt = Parameter.ParameterType;
            if (Nullable.GetUnderlyingType(pt) is not null) return false;
            if (!pt.IsValueType) return false;
            return true;
        }
    }

    public ParameterDescriptor(ParameterInfo parameter, ParameterBindingKind kind, object? serviceKey = null)
    {
        Parameter = parameter;
        WireName = parameter.Name ?? $"arg{parameter.Position}";
        Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
        Kind = kind;
        ServiceKey = serviceKey;
    }
}

internal enum ParameterBindingKind
{
    Argument,           // bind from request arguments[name]
    Service,            // resolve from IServiceProvider
    CancellationToken,  // pass the dispatch CancellationToken
    UriTemplate,        // bind from extracted URI template variable (resources only)
}
