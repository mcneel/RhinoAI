using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RhinoAI.Server;

// Builds a JSON Schema (draft-2020-12 flavour, the minimal subset MCP cares
// about) from a method's parameter list. Skips parameters supplied by the
// host (IServiceProvider-resolved + CancellationToken) since those never
// show up in the wire-level arguments object.
//
// Primitives map to a bare "type". Arrays carry `items` and complex types carry
// `properties`, because a tool taking a list of records (ask_user) is unusable
// without them: the agent would have to guess the element shape. Nested shapes
// come from the longest public constructor rather than from properties, so the
// schema and ParameterBinder's STJ deserialization agree on what is required
// and STJ has that same constructor to bind through.
internal static class SchemaBuilder
{
    // Deep enough for a list of records; past that a tool is asking the wrong question.
    private const int MaxDepth = 3;

    public static JsonElement BuildInputSchema(IReadOnlyList<ParameterDescriptor> descriptors)
    {
        JsonObject properties = new();
        JsonArray required = new();

        foreach (ParameterDescriptor p in descriptors)
        {
            if (!p.IncludeInSchema) continue;

            JsonObject prop = SchemaFor(p.ParameterType, 0, []);
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

    // The wire name a member is bound under. Must use the serializer's own policy or the advertised
    // schema and the binder disagree about the spelling of every nested field.
    public static string WireName(string name) =>
        McpSerializer.Options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    // Shared by ParameterDescriptor so a tool parameter and a nested constructor parameter are
    // judged required by exactly the same rules.
    public static bool IsRequired(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue) return false;
        Type pt = parameter.ParameterType;
        if (Nullable.GetUnderlyingType(pt) is not null) return false;
        if (pt.IsValueType) return true;
        // Reference type: a `string?` arg is optional, a `string` arg is required.
        // NullabilityInfoContext reads the NullableAttribute metadata emitted by
        // the compiler when Nullable=enable; falls back to "required" for assemblies
        // built without NRT (WriteState == Unknown).
        NullabilityInfoContext ctx = new();
        return ctx.Create(parameter).WriteState != NullabilityState.Nullable;
    }

    // `seen` is the path being expanded, not every type visited: an entry is removed on the way out
    // so two sibling fields of the same type both get a shape, while a cycle still terminates.
    private static JsonObject SchemaFor(Type t, int depth, HashSet<Type> seen)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;

        if (PrimitiveType(u) is string primitive)
            return new JsonObject { ["type"] = primitive };

        if (ElementType(u) is Type element)
        {
            JsonObject array = new() { ["type"] = "array" };
            if (depth < MaxDepth)
                array["items"] = SchemaFor(element, depth + 1, seen);
            return array;
        }

        JsonObject obj = new() { ["type"] = "object" };
        if (depth >= MaxDepth || !seen.Add(u))
            return obj;

        if (BindingConstructor(u) is ConstructorInfo ctor)
        {
            JsonObject properties = new();
            JsonArray required = new();
            foreach (ParameterInfo p in ctor.GetParameters())
            {
                string wire = WireName(p.Name ?? string.Empty);
                if (wire.Length == 0) continue;

                JsonObject prop = SchemaFor(p.ParameterType, depth + 1, seen);
                if (DescriptionOf(u, p) is string description)
                    prop["description"] = description;
                properties[wire] = prop;

                if (IsRequired(p)) required.Add(wire);
            }
            if (properties.Count > 0) obj["properties"] = properties;
            if (required.Count > 0) obj["required"] = required;
        }

        seen.Remove(u);
        return obj;
    }

    // A positional record's [Description] lands on the constructor parameter, but an explicit
    // [property: Description] lands on the generated property, so check both.
    private static string? DescriptionOf(Type owner, ParameterInfo p)
    {
        if (p.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } fromParameter)
            return fromParameter;
        if (p.Name is null)
            return null;
        return owner.GetProperty(p.Name)?.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } fromProperty
            ? fromProperty
            : null;
    }

    // The constructor STJ will bind through: the widest public one that actually takes arguments.
    // Framework types are left shapeless on purpose: Dictionary<,> and friends have constructors
    // taking capacities and comparers, and advertising those as fields would be a lie.
    private static ConstructorInfo? BindingConstructor(Type u)
    {
        string ns = u.Namespace ?? string.Empty;
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
            ns.StartsWith("Microsoft.", StringComparison.Ordinal))
            return null;

        ConstructorInfo? best = null;
        foreach (ConstructorInfo ctor in u.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            int count = ctor.GetParameters().Length;
            if (count > 0 && (best is null || count > best.GetParameters().Length))
                best = ctor;
        }
        return best;
    }

    private static string? PrimitiveType(Type u) => u switch
    {
        _ when u == typeof(string) || u == typeof(Guid) || u == typeof(Uri) ||
               u == typeof(DateTime) || u == typeof(DateTimeOffset) || u == typeof(TimeSpan) => "string",
        _ when u == typeof(bool) => "boolean",
        _ when u == typeof(byte) || u == typeof(sbyte) || u == typeof(short) || u == typeof(ushort) ||
               u == typeof(int) || u == typeof(uint) || u == typeof(long) || u == typeof(ulong) => "integer",
        _ when u == typeof(float) || u == typeof(double) || u == typeof(decimal) => "number",

        // Advertise as integer to match the binder: McpSerializer.Options
        // doesn't register JsonStringEnumConverter, so STJ only reads
        // enums from numbers. Switch this to "string" if/when the binder
        // grows string-enum support.
        { IsEnum: true } => "integer",
        _ => null,
    };

    private static Type? ElementType(Type u)
    {
        if (u.IsArray) return u.GetElementType();
        return IsCollectionType(u) ? u.GetGenericArguments()[0] : null;
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
    public bool IsRequired => SchemaBuilder.IsRequired(Parameter);

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
