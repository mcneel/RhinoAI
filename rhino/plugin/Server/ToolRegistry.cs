using System.Reflection;

using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server;

// Scan an assembly once at startup, build a name->handler map for every method
// decorated with [McpServerTool] inside a [McpServerToolType] class.
//
// ToolHandler is abstract: a tool is "something with MCP metadata, a schema, and a
// way to be invoked", not necessarily a MethodInfo on this assembly.
// ReflectionToolHandler below is the compiled-tool implementation and the only one
// this registry produces; tools contributed at runtime by other Rhino plug-ins use
// a different subclass (see Server/Extensibility/). The UI-thread marshalling
// policy lives on the base so both kinds obey exactly one copy of it.
internal sealed class ToolRegistry
{

    private Dictionary<string, ToolHandler> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ToolHandler> All => ByName.Values;

    public bool TryGet(string name, out ToolHandler handler) =>
        ByName.TryGetValue(name, out handler!);

    public static ToolRegistry Scan(Assembly assembly, IServiceProvider services)
    {
        ToolRegistry registry = new();
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                McpServerToolAttribute? toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is null)
                    continue;

                string name = toolAttr.Name ?? method.Name;
                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                bool marshalToUi = method.GetCustomAttribute<BackgroundThreadAttribute>() is null;
                bool inPanelOnly = method.GetCustomAttribute<InPanelOnlyAttribute>() is not null;

                ReflectionToolHandler handler = new(
                    method, name, toolAttr.Title, description,
                    toolAttr.ReadOnly, toolAttr.Destructive,
                    marshalToUi, inPanelOnly, services);

                if (!registry.ByName.TryAdd(name, handler))
                    throw new InvalidOperationException($"Duplicate MCP tool name: {name}");
            }
        }
        return registry;
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

// A tool the dispatcher can list and call. Metadata and the UI-thread marshalling
// policy live here; how the call actually happens is the subclass's business.
internal abstract class ToolHandler
{
    private readonly bool _marshalToUi;

    public string Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }

    // True for tools that only make sense to the in-Rhino panel agent (the
    // `/agent` endpoint); the external `/` endpoint hides them and refuses calls.
    public bool InPanelOnly { get; }

    // Set by the subclass constructor: compiled tools derive it from their
    // parameters, runtime-registered ones are handed it by the provider.
    public JsonElement InputSchema { get; protected set; }

    protected ToolHandler(
        string name, string? title, string? description,
        bool readOnly, bool destructive, bool marshalToUi, bool inPanelOnly)
    {
        Name = name;
        Title = title;
        Description = description;
        ReadOnly = readOnly;
        Destructive = destructive;
        _marshalToUi = marshalToUi;
        InPanelOnly = inPanelOnly;
    }

    public Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
    {
        if (!_marshalToUi)
            return InvokeCoreAsync(arguments, scope, ct);

        // Default policy for compiled tools: marshal to the Rhino UI thread. macOS's
        // AppKit aborts the process if any UI/document API is touched off the main
        // thread, and most tools manipulate RhinoDoc. Tools that opt out via
        // [BackgroundThread] take the direct path above. Note runtime-registered
        // tools default the other way — their provider does its own marshalling.
        TaskCompletionSource<CallToolResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(async () =>
        {
            try
            { tcs.SetResult(await InvokeCoreAsync(arguments, scope, ct).ConfigureAwait(false)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }

    protected abstract Task<CallToolResult> InvokeCoreAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct);

    protected static CallToolResult FormatResult(object? result) => result switch
    {
        null => new CallToolResult { Content = { ContentBlock.CreateText("") } },
        string s => new CallToolResult { Content = { ContentBlock.CreateText(s) } },
        ContentBlock cb => new CallToolResult { Content = { cb } },
        IEnumerable<ContentBlock> blocks => new CallToolResult { Content = blocks.ToList() },
        _ => new CallToolResult
        {
            Content = { ContentBlock.CreateText(JsonSerializer.Serialize(result, McpSerializer.Options)) }
        },
    };
}

// A tool compiled into this assembly: a [McpServerTool] method, its bound
// parameters and the schema derived from them.
internal sealed class ReflectionToolHandler : ToolHandler
{
    private readonly MethodInfo _method;
    private readonly ParameterDescriptor[] _parameters;

    public ReflectionToolHandler(
        MethodInfo method, string name, string? title, string? description,
        bool readOnly, bool destructive,
        bool marshalToUi, bool inPanelOnly, IServiceProvider services)
        : base(name, title, description, readOnly, destructive, marshalToUi, inPanelOnly)
    {
        _method = method;

        _parameters = method.GetParameters()
            .Select(pi => ResolveBinding(pi, services))
            .ToArray();

        InputSchema = SchemaBuilder.BuildInputSchema(_parameters);
    }

    private static ParameterDescriptor ResolveBinding(ParameterInfo pi, IServiceProvider services)
    {
        if (pi.ParameterType == typeof(CancellationToken))
            return new ParameterDescriptor(pi, ParameterBindingKind.CancellationToken);

        // Anything we can resolve from DI is treated as a service. Falls back
        // to Argument binding for everything else (primitives + user types).
        // This mirrors RhinoDoc-injection used by every doc-aware tool.
        if (services.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsService))
                is Microsoft.Extensions.DependencyInjection.IServiceProviderIsService ispis
            && ispis.IsService(pi.ParameterType))
            return new ParameterDescriptor(pi, ParameterBindingKind.Service);

        if (services.GetService(pi.ParameterType) is not null)
            return new ParameterDescriptor(pi, ParameterBindingKind.Service);

        return new ParameterDescriptor(pi, ParameterBindingKind.Argument);
    }

    protected override async Task<CallToolResult> InvokeCoreAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
    {
        object?[] args = new object?[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
            args[i] = ParameterBinder.Resolve(_parameters[i], arguments, scope, ct);

        object? rawResult;
        try
        {
            rawResult = _method.Invoke(_method.IsStatic ? null : scope.GetService(_method.DeclaringType!), args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }

        object? result = await ResultUnwrapper.UnwrapAsync(rawResult).ConfigureAwait(false);
        return FormatResult(result);
    }
}
