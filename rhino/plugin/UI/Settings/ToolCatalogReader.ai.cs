using System.IO;
using System.Reflection;
using System.Threading;

using Rhino.Runtime;
using Rhino.UI;

using Rhino.AI.PlugIns;

namespace Rhino.AI.UI.Settings;

internal static class ToolCatalogReader
{
    public const string BuiltinServerName = "Rhino";

    private const string SlotArgument = "slot";
    private const string AnyTool = "*";
    private const BindingFlags ToolMethods = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static string EmDash { get; } = ((char)0x2014).ToString();
    private static string EnDash { get; } = ((char)0x2013).ToString();

    private sealed record RouterTool(string Name, ToolEffect Effect, IReadOnlyList<string> Args);

    private static IReadOnlyList<RouterTool> RouterTools { get; } =
    [
        new("list_slots", ToolEffect.Read, []),
        new("spawn_slot", ToolEffect.Edit, ["version"]),
        new("close_slot", ToolEffect.Destructive, [SlotArgument]),
    ];

    public static ToolCatalog Current(IReadOnlyList<ToolRule> rules, PlugInNames names)
        => new(Builtin(rules), ServerOf(new DefaultToolsMcp(null!, PlugInToken.Invalid), LOC.STR("Default tools")), PlugIns(names));

    private static ServerInfo Builtin(IReadOnlyList<ToolRule> rules)
    {
        Dictionary<string, ToolInfo> tools = new(StringComparer.Ordinal);
        foreach (ToolInfo tool in ScanPlugInTools())
        {
            if (!tools.ContainsKey(tool.Name))
                tools.Add(tool.Name, tool);
        }

        foreach (RouterTool tool in RouterTools)
        {
            if (!tools.ContainsKey(tool.Name))
                tools.Add(tool.Name, new ToolInfo(tool.Name, Humanize(tool.Name), string.Empty, tool.Effect, tool.Args));
        }

        foreach (ToolRule rule in rules)
        {
            if (rule.Server != BuiltinServerName || rule.Tool == AnyTool || tools.ContainsKey(rule.Tool))
                continue;
            tools.Add(rule.Tool, new ToolInfo(rule.Tool, Humanize(rule.Tool), string.Empty, ToolEffect.Edit, rule.Args.Select(a => a.Name).ToList()));
        }

        return new ServerInfo(BuiltinServerName, BuiltinServerName, McpTransport.InProcess, tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList());
    }

    private static List<ToolInfo> ScanPlugInTools()
    {
        List<ToolInfo> tools = [];
        foreach (Type type in LoadableTypes(typeof(ToolCatalogReader).Assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (MethodInfo method in type.GetMethods(ToolMethods))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not McpServerToolAttribute attribute)
                    continue;

                string name = attribute.Name ?? method.Name;
                if (name.StartsWith("_", StringComparison.Ordinal))
                    continue;

                tools.Add(new ToolInfo(
                    name,
                    attribute.Title is string title && title.Trim().Length > 0 ? Plain(title) : Humanize(name),
                    Plain(method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty),
                    EffectOf(attribute.ReadOnly, attribute.Destructive),
                    ArgumentsOf(method)));
            }
        }
        return tools;
    }

    // The router appends slot to every tool it proxies, and the default rules in UserPreferences list it too.
    private static IReadOnlyList<string> ArgumentsOf(MethodInfo method)
    {
        List<string> args = [];
        foreach (ParameterInfo parameter in method.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken) || parameter.ParameterType == typeof(RhinoDoc))
                continue;
            args.Add(parameter.Name ?? $"arg{parameter.Position}");
        }

        args.Add(SlotArgument);
        return args;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static IReadOnlyList<PlugInInfo> PlugIns(PlugInNames names)
    {
        List<PlugInToken> tokens = AIInstances.Registry.PlugIns.Keys
            .Concat(PlugInRegistry.PrivatePanelMcps.Keys)
            .Concat(PlugInRegistry.PrivatePanelSkills.Keys)
            .GroupBy(token => token.Id)
            .Select(group => group.First())
            .ToList();

        List<PlugInInfo> loaded = tokens
            .Select(token => Loaded(token, names))
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        HashSet<Guid> shown = new(tokens.Select(token => token.Id));
        List<PlugInInfo> notLoaded = PlugInRegistry.SavedEntries
            .Where(saved => names.IsInstalled(saved.Id) && shown.Add(saved.Id))
            .Select(saved => new PlugInInfo(saved.Id.ToString(), names.Of(saved.Id, saved.Name), saved.Enabled, false, null, [], []))
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return [.. loaded, .. notLoaded];
    }

    private static PlugInInfo Loaded(PlugInToken token, PlugInNames names)
    {
        AIInstances.Registry.PlugIns.TryGetValue(token, out PlugInRegistry.AgenticPlugIn? agentic);
        PlugInRegistry.PrivatePanelMcps.TryGetValue(token, out List<Mcp>? mcps);
        PlugInRegistry.PrivatePanelSkills.TryGetValue(token, out List<ISkill>? skills);

        return new PlugInInfo(
            token.Id.ToString(),
            names.Of(token.Id, token.Name),
            PlugInRegistry.IsAllowed(token),
            true,
            agentic is null ? null : IconOf(agentic.Assembly, agentic.IconResource),
            (mcps ?? []).Select(mcp => ServerOf(mcp, mcp.Name)).ToList(),
            (skills ?? []).Select(skill => new SkillInfo(skill.Name, Plain(skill.Description ?? string.Empty))).ToList());
    }

    private static ServerInfo ServerOf(Mcp mcp, string title) => new(
        mcp.Name,
        title,
        mcp switch
        {
            HttpMcp => McpTransport.Http,
            StdioMcp => McpTransport.Stdio,
            _ => McpTransport.InProcess,
        },
        mcp.Tools.Values
            .Select(tool => new ToolInfo(
                tool.Name,
                Humanize(tool.Name),
                Plain(tool.Description ?? string.Empty),
                EffectOf(tool.ReadOnly, tool.Destructive),
                tool.Args.Select(arg => arg.Name).ToList()))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList());

    private static string? IconOf(Assembly assembly, string? resource)
    {
        if (resource is null || resource.Length == 0)
            return null;

        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
                return null;

            using System.Drawing.Icon icon = new(stream);
            using System.Drawing.Bitmap bitmap = icon.ToBitmap();
            using Eto.Drawing.Bitmap png = bitmap.ToEto();
            return "data:image/png;base64," + Convert.ToBase64String(png.ToByteArray(Eto.Drawing.ImageFormat.Png));
        }
        catch (Exception ex)
        {
            HostUtils.LogDebugEvent($"[rhino-ai] AI settings could not read the icon '{resource}': {ex.Message}.\n");
            return null;
        }
    }

    private static ToolEffect EffectOf(bool readOnly, bool destructive) => (readOnly, destructive) switch
    {
        (true, _) => ToolEffect.Read,
        (false, true) => ToolEffect.Destructive,
        (false, false) => ToolEffect.Edit,
    };

    private static string Plain(string text) => text
        .Replace(" " + EmDash + " ", ", ")
        .Replace(" " + EnDash + " ", ", ")
        .Replace(EmDash, ", ")
        .Replace(EnDash, " to ");

    private static string Humanize(string name)
    {
        string spaced = name.Replace('_', ' ').Trim();
        return spaced.Length == 0 ? name : char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }
}
