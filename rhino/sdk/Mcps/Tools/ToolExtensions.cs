using System;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

internal static class ToolExtensions
{

    public static bool TryGetString(this IReadOnlyList<IToolArg> args, string name, out string value)
    {
        value = string.Empty;
        if (Find(args, name) is not ToolString text) return false;

        value = text.Value;
        return true;
    }

    public static bool TryGetPath(this IReadOnlyList<IToolArg> args, string name, out string value)
    {
        value = string.Empty;
        if (Find(args, name) is not ToolPath path) return false;

        value = path.Value;
        return true;
    }

    public static bool TryGetUrl(this IReadOnlyList<IToolArg> args, string name, out string value)
    {
        value = string.Empty;
        if (Find(args, name) is not ToolUrl url) return false;

        value = url.Value;
        return true;
    }

    public static bool TryGetSecret(this IReadOnlyList<IToolArg> args, string name, out string value)
    {
        value = string.Empty;
        if (Find(args, name) is not ToolSecret secret) return false;

        value = secret.Value;
        return true;
    }

    public static bool TryGetInt(this IReadOnlyList<IToolArg> args, string name, out int value)
    {
        value = 0;
        if (Find(args, name) is not ToolInt integer) return false;

        value = integer.Value;
        return true;
    }

    public static bool TryGetNumber(this IReadOnlyList<IToolArg> args, string name, out double value)
    {
        value = 0;
        if (Find(args, name) is not ToolNumber number) return false;

        value = number.Value;
        return true;
    }

    public static bool TryGetBoolean(this IReadOnlyList<IToolArg> args, string name, out bool value)
    {
        value = false;
        if (Find(args, name) is not ToolBoolean boolean) return false;

        value = boolean.Value;
        return true;
    }

    private static IToolArg? Find(IReadOnlyList<IToolArg> args, string name)
    {
        foreach (IToolArg arg in args)
        {
            if (string.Equals(arg.Name, name, StringComparison.OrdinalIgnoreCase))
                return arg;
        }

        return null;
    }

}
