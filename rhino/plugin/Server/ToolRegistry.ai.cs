using System.Reflection;

using System.Threading;
using System.Threading.Tasks;

using Rhino.AI.Tools;

namespace Rhino.AI.Server;

/// <summary>
/// Registers all of the MCP Tools
/// </summary>
internal sealed class ToolRegistry
{

    private Dictionary<string, ToolHandler> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ToolHandler> All => ByName.Values;

    public bool TryGet(string name, out ToolHandler handler) =>
        ByName.TryGetValue(name, out handler!);


    const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static ToolRegistry Scan(Assembly assembly, IServiceProvider services)
    {
        ToolRegistry registry = new();
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type?.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (MethodInfo method in type.GetMethods(FLAGS))
            {
                McpServerToolAttribute? toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is null) continue;

                string name = toolAttr.Name ?? method.Name;

                if (!typeof(IToolResult).IsAssignableFrom(ResultType(method)))
                    throw new InvalidOperationException($"MCP tool '{name}' returns {method.ReturnType.Name}; every tool must return an IToolResult.");

                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                bool marshalToUi = method.GetCustomAttribute<BackgroundThreadAttribute>() is null;
                bool inPanelOnly = method.GetCustomAttribute<InPanelOnlyAttribute>() is not null;

                ToolHandler handler = new(
                    method, name, toolAttr.Title, description,
                    toolAttr.ReadOnly, toolAttr.Destructive,
                    marshalToUi, inPanelOnly, services);

                if (!registry.ByName.TryAdd(name, handler))
                    throw new InvalidOperationException($"Duplicate MCP tool name: {name}");
            }
        }
        return registry;
    }

    // What the tool actually hands back once a Task/ValueTask wrapper is peeled off.
    private static Type ResultType(MethodInfo method)
    {
        Type returned = method.ReturnType;
        if (!returned.IsGenericType)
            return returned;

        Type definition = returned.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>)
            ? returned.GetGenericArguments()[0]
            : returned;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

internal sealed class ToolHandler
{
    private readonly MethodInfo _method;
    private readonly ParameterDescriptor[] _parameters;
    private readonly bool _marshalToUi;

    public string Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }

    // True for tools that only make sense to the in-Rhino panel agent (the
    // `/agent` endpoint); the external `/` endpoint hides them and refuses calls.
    public bool InPanelOnly { get; }

    public JsonElement InputSchema { get; }

    public ToolHandler(
        MethodInfo method, string name, string? title, string? description,
        bool readOnly, bool destructive,
        bool marshalToUi, bool inPanelOnly, IServiceProvider services)
    {
        _method = method;
        Name = name;
        Title = title;
        Description = description;
        ReadOnly = readOnly;
        Destructive = destructive;
        _marshalToUi = marshalToUi;
        InPanelOnly = inPanelOnly;

        _parameters = method.GetParameters()
            .Select(pi => ResolveBinding(pi, services))
            .ToArray();

        InputSchema = SchemaBuilder.BuildInputSchema(_parameters);
    }

    private static ParameterDescriptor ResolveBinding(ParameterInfo pi, IServiceProvider services)
    {
        if (pi.ParameterType == typeof(CancellationToken))
            return new ParameterDescriptor(pi, ParameterBindingKind.CancellationToken);

        if (services.GetService(pi.ParameterType) is not null)
            return new ParameterDescriptor(pi, ParameterBindingKind.Service);

        return new ParameterDescriptor(pi, ParameterBindingKind.Argument);
    }

    public Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
    {
        if (!_marshalToUi)
            return InvokeCoreAsync(arguments, scope, ct);

        // Default policy: marshal every tool to the Rhino UI thread. macOS's
        // AppKit aborts the process if any UI/document API is touched off the
        // main thread, and most tools manipulate RhinoDoc. Tools that opt out
        // via [BackgroundThread] take the direct path above.
        TaskCompletionSource<CallToolResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(async () =>
        {
            try
            { tcs.SetResult(await InvokeCoreAsync(arguments, scope, ct).ConfigureAwait(false)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }));
        return tcs.Task;
    }

    private static bool GH2Loaded { get; set; } = false;

    private async Task<CallToolResult> InvokeCoreAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
    {
        object?[] args = new object?[_parameters.Length];
        IReadOnlyList<string> coercions;

        using (BindNotes.Call call = BindNotes.Begin())
        {
            List<ArgumentBindingException> failures = [];

            for (int i = 0; i < _parameters.Length; i++)
            {
                try
                {
                    args[i] = ParameterBinder.Resolve(_parameters[i], arguments, scope, ct);
                }
                catch (ArgumentBindingException ex)
                {
                    failures.Add(ex);
                }
            }

            if (failures.Count > 0)
                return ToolResultFormatter.Format(BindingFailure.Describe(Name, _parameters, failures));

            coercions = call.Notes;
        }

        object? rawResult;
        try
        {
            #if R9
            EnsureGh2IsLoaded(Name, args);
            #endif

            rawResult = _method.Invoke(_method.IsStatic ? null : scope.GetService(_method.DeclaringType!), args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is OverflowException overflow)
        {
            return ToolResultFormatter.Format(ToolResult.Failure(
                ToolError.BadArgument,
                overflow.Message,
                "A value exceeded the range of the type it was applied to; pass a smaller number."));
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }

        object? result = await ResultUnwrapper.UnwrapAsync(rawResult).ConfigureAwait(false);

        // Scan rejects any tool that isn't typed to return one, so this only trips
        // on a tool returning a null IToolResult.
        if (result is not IToolResult toolResult)
            throw new InvalidOperationException($"Tool '{Name}' returned no result.");

        return ToolResultFormatter.Format(toolResult, Advisories(coercions, arguments));
    }

    private string? Advisories(IReadOnlyList<string> coercions, IDictionary<string, JsonElement>? arguments)
    {
        string? ignored = IgnoredArguments(arguments);
        if (coercions.Count == 0)
            return ignored;

        string coerced = string.Join("; ", coercions);
        return ignored is null ? coerced : $"{coerced}; {ignored}";
    }

    // Unclaimed arguments are honoured as far as they can be (dropped) rather than refused,
    // so the caller is told instead of watching a call succeed and change nothing.
    private string? IgnoredArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return null;

        string[] accepted = _parameters
            .Where(p => p.IncludeInSchema)
            .Select(p => p.WireName)
            .ToArray();

        string[] ignored = arguments.Keys
            .Where(name => !accepted.Contains(name, StringComparer.Ordinal))
            .ToArray();

        if (ignored.Length == 0)
            return null;

        string names = string.Join(", ", ignored.Select(n => $"'{n}'"));
        string subject = ignored.Length == 1 ? "it is not an argument" : "they are not arguments";

        return $"Ignored {names} because {subject} '{Name}' accepts. Its arguments are: {string.Join(", ", accepted)}";
    }

    private static void EnsureGh2IsLoaded(string toolName, object?[] args)
    {
#if R9
        if (GH2Loaded) return;
        if (args is null) return;
        if (args.Length < 1) return;
        if (args[0] is not RhinoDoc doc) return;
        if (string.IsNullOrEmpty(toolName)) return;
        if (!toolName.Contains("G2_", StringComparison.OrdinalIgnoreCase)) return;

        GH2Loaded = !GH2_StartTool.Launch(doc).IsFailure;
#endif
    }
}
