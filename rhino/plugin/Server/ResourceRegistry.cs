using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server;

// Resource side of the dispatcher. Same shape as ToolRegistry, but with two
// extra wrinkles: each resource has a URI (static literal or RFC-6570-level-1
// template) which we have to match against an incoming `uri`; and template
// variables are bound to method parameters by name.
internal sealed class ResourceRegistry
{
    private List<ResourceHandler> Handlers { get; } = new();

    public IReadOnlyList<ResourceHandler> All => Handlers;

    public IReadOnlyList<ResourceHandler> StaticResources =>
        Handlers.Where(h => !h.IsTemplated).ToList();

    public IReadOnlyList<ResourceHandler> Templated =>
        Handlers.Where(h => h.IsTemplated).ToList();

    public ResourceHandler? Match(string uri, out IReadOnlyDictionary<string, string> variables)
    {
        foreach (ResourceHandler h in Handlers)
        {
            if (h.TryMatch(uri, out variables!))
                return h;
        }
        variables = new Dictionary<string, string>();
        return null;
    }

    public static ResourceRegistry Scan(Assembly assembly, IServiceProvider services)
    {
        ResourceRegistry registry = new();

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (Type type in SafeGetTypes(assembly))
        {
            if (type.GetCustomAttribute<McpServerResourceTypeAttribute>() is null)
                continue;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                McpServerResourceAttribute? resAttr = method.GetCustomAttribute<McpServerResourceAttribute>();
                if (resAttr is null)
                    continue;

                if (string.IsNullOrEmpty(resAttr.UriTemplate))
                    throw new InvalidOperationException(
                        $"{type.FullName}.{method.Name}: McpServerResource attribute requires UriTemplate.");

                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                registry.Handlers.Add(new ResourceHandler(
                    method,
                    uriTemplate: resAttr.UriTemplate!,
                    name: resAttr.Name ?? method.Name,
                    description: description,
                    mimeType: resAttr.MimeType,
                    services: services));
            }
        }
        return registry;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}

internal sealed class ResourceHandler
{
    private readonly MethodInfo _method;
    private readonly ParameterDescriptor[] _parameters;
    private readonly Regex? _matcher;
    private readonly string[] _variables;

    public string UriTemplate { get; }
    public string Name { get; }
    public string? Description { get; }
    public string? MimeType { get; }
    public bool IsTemplated => _matcher is not null;

    public ResourceHandler(
        MethodInfo method, string uriTemplate, string name, string? description,
        string? mimeType, IServiceProvider services)
    {
        _method = method;
        UriTemplate = uriTemplate;
        Name = name;
        Description = description;
        MimeType = mimeType;

        (_matcher, _variables) = CompileUriTemplate(uriTemplate);

        _parameters = method.GetParameters()
            .Select(pi => ResolveBinding(pi, services))
            .ToArray();
    }

    // Walk the template once, splitting on '{var}' segments. Each literal chunk
    // is regex-escaped; each variable becomes a named capture group consuming
    // anything except '/'. Returns a null Regex for purely literal templates,
    // since we can short-circuit those with string equality.
    private static (Regex? matcher, string[] variables) CompileUriTemplate(string template)
    {
        List<string> vars = new();
        StringBuilder pattern = new("^");

        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0)
            {
                pattern.Append(Regex.Escape(template.Substring(i)));
                break;
            }
            pattern.Append(Regex.Escape(template.Substring(i, open - i)));

            int close = template.IndexOf('}', open + 1);
            if (close < 0)
                throw new InvalidOperationException(
                    $"Unterminated '{{' in URI template '{template}'.");

            string name = template.Substring(open + 1, close - open - 1);
            if (name.Length == 0)
                throw new InvalidOperationException(
                    $"Empty variable name in URI template '{template}'.");

            vars.Add(name);
            pattern.Append("(?<").Append(name).Append(">[^/]+)");
            i = close + 1;
        }
        pattern.Append("$");

        if (vars.Count == 0)
            return (null, Array.Empty<string>());

        return (new Regex(pattern.ToString(), RegexOptions.Compiled), vars.ToArray());
    }

    private ParameterDescriptor ResolveBinding(ParameterInfo pi, IServiceProvider services)
    {
        if (pi.ParameterType == typeof(CancellationToken))
            return new ParameterDescriptor(pi, ParameterBindingKind.CancellationToken);

        if (pi.Name is { } pname && Array.IndexOf(_variables, pname) >= 0)
            return new ParameterDescriptor(pi, ParameterBindingKind.UriTemplate);

        if (services.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsService))
                is Microsoft.Extensions.DependencyInjection.IServiceProviderIsService ispis
            && ispis.IsService(pi.ParameterType))
            return new ParameterDescriptor(pi, ParameterBindingKind.Service);

        if (services.GetService(pi.ParameterType) is not null)
            return new ParameterDescriptor(pi, ParameterBindingKind.Service);

        // Resources don't take wire-level arguments outside template variables,
        // so anything else falls back to default-value or null.
        return new ParameterDescriptor(pi, ParameterBindingKind.Argument);
    }

    public bool TryMatch(string uri, out IReadOnlyDictionary<string, string> values)
    {
        if (_matcher is null)
        {
            if (string.Equals(uri, UriTemplate, StringComparison.Ordinal))
            {
                values = new Dictionary<string, string>();
                return true;
            }
            values = new Dictionary<string, string>();
            return false;
        }

        Match match = _matcher.Match(uri);
        if (!match.Success)
        { values = new Dictionary<string, string>(); return false; }

        Dictionary<string, string> dict = new(_variables.Length, StringComparer.Ordinal);
        foreach (string v in _variables)
            dict[v] = match.Groups[v].Value;
        values = dict;
        return true;
    }

    public async Task<ReadResourceResult> InvokeAsync(
        string uri, IReadOnlyDictionary<string, string> variables, IServiceProvider scope, CancellationToken ct)
    {
        object?[] args = new object?[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
            args[i] = ParameterBinder.Resolve(_parameters[i], arguments: null, scope, ct, variables);

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

        // Resources return strings or arbitrary objects. Strings become text
        // content; everything else gets JSON-serialised into the text slot.
        string text = result switch
        {
            null => "",
            string s => s,
            _ => JsonSerializer.Serialize(result, McpSerializer.Options),
        };

        return new ReadResourceResult
        {
            Contents = { new ResourceContent { Uri = uri, MimeType = MimeType, Text = text } }
        };
    }
}
