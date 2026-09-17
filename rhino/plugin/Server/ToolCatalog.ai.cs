using System.Reflection;

namespace Rhino.AI.Server;

// What a tool declares. Behaviour is the Read-only / Modify / Destructive reading of its annotations;
// Group is the label a [ToolGroup] tool type gives its tools, null for the rest.
internal sealed record ToolInfo(
    string Name, string Title, string Description, string Behaviour, string? Group, ToolMode DefaultMode);

// Mirror of ToolRegistry.Scan that reads names, descriptions and defaults without instantiating tools
// or building schemas (Scan needs an IServiceProvider for those). Router-internal tools (leading
// underscore) are excluded so they can never be switched off.
internal static class ToolCatalog
{
    private const BindingFlags Flags =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static IReadOnlyList<ToolInfo>? Cached { get; set; }

    public static IReadOnlyList<ToolInfo> All => Cached ??= Scan(typeof(ToolCatalog).Assembly);

    public static bool TryGet(string name, out ToolInfo tool)
    {
        foreach (ToolInfo candidate in All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                tool = candidate;
                return true;
            }
        }
        tool = default!;
        return false;
    }

    public static string Behaviour(McpServerToolAttribute attribute) =>
        attribute.ReadOnly ? "Read-only" : attribute.Destructive ? "Destructive" : "Modify";

    private static IReadOnlyList<ToolInfo> Scan(Assembly assembly)
    {
        List<ToolInfo> tools = [];
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            string? group = type.GetCustomAttribute<ToolGroupAttribute>()?.Label;

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not McpServerToolAttribute attribute)
                    continue;

                string name = attribute.Name ?? method.Name;
                if (name.StartsWith('_'))
                    continue;

                tools.Add(new ToolInfo(
                    name,
                    string.IsNullOrWhiteSpace(attribute.Title) ? name : attribute.Title!,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    Behaviour(attribute),
                    group,
                    ToolModes.DefaultFor(attribute.EnabledByDefault, attribute.ConfirmByDefault)));
            }
        }
        return tools;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
