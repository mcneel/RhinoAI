#if NETFRAMEWORK
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI
{

    internal static class OperatingSystem
    {
        public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsMacOS() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    internal static class MathF
    {
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Max(float x, float y) => Math.Max(x, y);
        public static float Min(float x, float y) => Math.Min(x, y);
    }

    internal static class NetFrameworkExtensions
    {
        public static bool Contains(this string s, string value, StringComparison comparison) => s.IndexOf(value, comparison) >= 0;

        public static bool Contains(this string s, char value) => s.IndexOf(value) >= 0;

        public static bool StartsWith(this string s, char value) => s.Length != 0 && s[0] == value;

        public static bool EndsWith(this string s, char value) => s.Length != 0 && s[s.Length - 1] == value;

        public static string[] Split(this string s, string separator, StringSplitOptions options = StringSplitOptions.None) =>
            s.Split([separator], options);

        public static string[] Split(this string s, char separator, StringSplitOptions options) =>
            s.Split([separator], options);

        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (dictionary.ContainsKey(key))
                return false;
            dictionary.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
            where TKey : notnull
        {
            if (!dictionary.TryGetValue(key, out value))
                return false;
            dictionary.Remove(key);
            return true;
        }

        public static bool TryDequeue<T>(this Queue<T> queue, out T result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }
            result = queue.Dequeue();
            return true;
        }
    }
}

namespace System.Runtime.CompilerServices
{

    internal static class IsExternalInit { }

}

#endif
