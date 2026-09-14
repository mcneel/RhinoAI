using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

internal static class ToolExtensions
{

    public static bool TryGetAs<T>(this IReadOnlyDictionary<string, object> dict, string key, out T value)
    {
        value = default!;
        if (dict is null) return false;
        
        if (!dict.TryGetValue(key, out object? obj) && !dict.TryGetValue(key.ToLowerInvariant(), out obj)) return false;
        if( obj is not T cast) return false;

        value = cast;

        return true;
    }

}
