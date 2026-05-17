using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server;

// Resolves a single parameter's value at invocation time. Used by both
// ToolHandler and ResourceHandler — the binding rules are the same except
// resources never expect a wire-arguments dictionary, and may have URI
// template variables that supply parameters by name.
internal static class ParameterBinder
{
    public static object? Resolve(
        ParameterDescriptor p,
        IDictionary<string, JsonElement>? arguments,
        IServiceProvider services,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? uriTemplateValues = null)
    {
        switch (p.Kind)
        {
            case ParameterBindingKind.CancellationToken:
                return cancellationToken;

            case ParameterBindingKind.Service:
                return services.GetService(p.ParameterType)
                    ?? (p.Parameter.HasDefaultValue
                        ? p.Parameter.DefaultValue
                        : throw new ArgumentException(
                            $"No service of type {p.ParameterType.FullName} was registered for parameter '{p.WireName}'."));

            case ParameterBindingKind.UriTemplate:
                if (uriTemplateValues is null || !uriTemplateValues.TryGetValue(p.WireName, out var raw))
                {
                    if (p.Parameter.HasDefaultValue) return p.Parameter.DefaultValue;
                    throw new ArgumentException(
                        $"URI template variable '{p.WireName}' was not present in the request URI.");
                }
                return ConvertString(raw, p.ParameterType);

            case ParameterBindingKind.Argument:
                if (arguments is null || !arguments.TryGetValue(p.WireName, out var element))
                {
                    if (p.Parameter.HasDefaultValue) return p.Parameter.DefaultValue;
                    if (IsNullable(p.Parameter)) return null;
                    throw new ArgumentException($"Required argument '{p.WireName}' was not supplied.");
                }
                if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    if (p.Parameter.HasDefaultValue) return p.Parameter.DefaultValue;
                    if (IsNullable(p.Parameter)) return null;
                    throw new ArgumentException($"Argument '{p.WireName}' was null but the parameter is not nullable.");
                }
                return JsonSerializer.Deserialize(element.GetRawText(), p.ParameterType, McpSerializer.Options);

            default:
                throw new InvalidOperationException($"Unknown binding kind {p.Kind}");
        }
    }

    private static object? ConvertString(string raw, Type target)
    {
        Type t = Nullable.GetUnderlyingType(target) ?? target;
        if (t == typeof(string)) return raw;
        if (t == typeof(Guid)) return Guid.Parse(raw);
        if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);
        return Convert.ChangeType(raw, t, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsNullable(ParameterInfo pi)
    {
        if (!pi.ParameterType.IsValueType) return true;
        return Nullable.GetUnderlyingType(pi.ParameterType) is not null;
    }
}

// Awaits Task/Task<T>/ValueTask/ValueTask<T> uniformly and returns the inner
// value (or null for non-generic completions).
internal static class ResultUnwrapper
{
    public static async ValueTask<object?> UnwrapAsync(object? raw)
    {
        switch (raw)
        {
            case null: return null;

            case Task task:
                await task.ConfigureAwait(false);
                var taskType = task.GetType();
                if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                    return taskType.GetProperty("Result")?.GetValue(task);
                return null;

            case ValueTask vt:
                await vt.ConfigureAwait(false);
                return null;
        }

        // ValueTask<T> is a struct so it doesn't hit the type-pattern above.
        var rt = raw.GetType();
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var converted = (Task)rt.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(raw, null)!;
            await converted.ConfigureAwait(false);
            return converted.GetType().GetProperty("Result")?.GetValue(converted);
        }

        return raw;
    }
}
