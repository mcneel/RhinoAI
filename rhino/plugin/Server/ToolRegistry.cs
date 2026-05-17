using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server;

// Scan an assembly once at startup, build a name->handler map for every method
// decorated with [McpServerTool] inside a [McpServerToolType] class. ToolHandler
// owns its bound parameters, its schema, and the per-tool decision about whether
// to marshal the invocation onto the Rhino UI thread.
internal sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolHandler> _byName = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ToolHandler> All => _byName.Values;

    public bool TryGet(string name, out ToolHandler handler) =>
        _byName.TryGetValue(name, out handler!);

    public static ToolRegistry Scan(Assembly assembly, IServiceProvider services)
    {
        ToolRegistry registry = new();
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null) continue;

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                McpServerToolAttribute? toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is null) continue;

                string name = toolAttr.Name ?? method.Name;
                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                bool marshalToUi = method.GetCustomAttribute<BackgroundThreadAttribute>() is null;

                ToolHandler handler = new(method, name, toolAttr.Title, description, marshalToUi, services);
                if (!registry._byName.TryAdd(name, handler))
                    throw new InvalidOperationException($"Duplicate MCP tool name: {name}");
            }
        }
        return registry;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
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
    public JsonElement InputSchema { get; }

    public ToolHandler(
        MethodInfo method, string name, string? title, string? description,
        bool marshalToUi, IServiceProvider services)
    {
        _method = method;
        Name = name;
        Title = title;
        Description = description;
        _marshalToUi = marshalToUi;

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

    public Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, IServiceProvider scope, CancellationToken ct)
    {
        if (!_marshalToUi) return InvokeCoreAsync(arguments, scope, ct);

        // Default policy: marshal every tool to the Rhino UI thread. macOS's
        // AppKit aborts the process if any UI/document API is touched off the
        // main thread, and most tools manipulate RhinoDoc. Tools that opt out
        // via [BackgroundThread] take the direct path above.
        TaskCompletionSource<CallToolResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(async () =>
        {
            try { tcs.SetResult(await InvokeCoreAsync(arguments, scope, ct).ConfigureAwait(false)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }

    private async Task<CallToolResult> InvokeCoreAsync(
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

    private static CallToolResult FormatResult(object? result) => result switch
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
