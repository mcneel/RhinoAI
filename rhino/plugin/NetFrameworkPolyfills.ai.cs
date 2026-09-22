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
        public static float Pow(float x, float y) => (float)System.Math.Pow(x, y);
        public static float Max(float x, float y) => System.Math.Max(x, y);
        public static float Min(float x, float y) => System.Math.Min(x, y);
    }

    internal static class Math
    {
        
        public static float Clamp(float input, float min, float max)
            => input switch
            {
                _ when input > max => max,
                _ when input < min => min,
                
                _ => input
            };
        
        public static double Clamp(double input, double min, double max)
            => input switch
            {
                _ when input > max => max,
                _ when input < min => min,
                
                _ => input
            };
            
        public static decimal Clamp(decimal input, decimal min, decimal max)
            => input switch
            {
                _ when input > max => max,
                _ when input < min => min,
                
                _ => input
            };
            
        public static int Clamp(int input, int min, int max)
            => input switch
            {
                _ when input > max => max,
                _ when input < min => min,
                
                _ => input
            };

        public static int Max(int one, int two) => one > two ? one : two;

        public static double Round(double input) => System.Math.Round(input);

    }

    internal static class Double
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal static class String
    {
        public static bool IsNullOrEmpty([NotNullWhen(false)] string? value) => string.IsNullOrEmpty(value);

        public static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);
    }

    internal enum NullabilityState { Unknown, NotNull, Nullable }

    internal sealed class NullabilityInfo
    {
        public NullabilityInfo(NullabilityState state) => ReadState = WriteState = state;

        public NullabilityState ReadState { get; }

        public NullabilityState WriteState { get; }
    }

    // Top-level parameter nullability only, which is all IsRequired/IsNullable ask for. The real
    // BCL type also walks generic arguments and array ranks; nothing here needs that. The
    // NullableAttribute/NullableContextAttribute Roslyn emits are internal to the assembly that
    // carries them, so they are matched by name through CustomAttributeData rather than referenced.
    internal sealed class NullabilityInfoContext
    {
        private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";
        private const string NullableContextAttributeName = "System.Runtime.CompilerServices.NullableContextAttribute";

        public NullabilityInfo Create(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;
            if (parameterType.IsValueType)
                return new NullabilityInfo(System.Nullable.GetUnderlyingType(parameterType) is not null
                    ? NullabilityState.Nullable
                    : NullabilityState.NotNull);

            // An explicit [Nullable] on the parameter wins; otherwise the nearest enclosing
            // [NullableContext] - method, declaring types outwards, then module.
            if (Flag(parameter.GetCustomAttributesData(), NullableAttributeName) is byte own)
                return new NullabilityInfo(State(own));

            MemberInfo member = parameter.Member;
            if (Flag(member.GetCustomAttributesData(), NullableContextAttributeName) is byte method)
                return new NullabilityInfo(State(method));

            for (Type? declaring = member.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
                if (Flag(declaring.GetCustomAttributesData(), NullableContextAttributeName) is byte enclosing)
                    return new NullabilityInfo(State(enclosing));

            if (Flag(member.Module.GetCustomAttributesData(), NullableContextAttributeName) is byte module)
                return new NullabilityInfo(State(module));

            return new NullabilityInfo(NullabilityState.Unknown);
        }

        private static NullabilityState State(byte flag) => flag switch
        {
            1 => NullabilityState.NotNull,
            2 => NullabilityState.Nullable,
            _ => NullabilityState.Unknown, // 0 = oblivious
        };

        private static byte? Flag(IList<CustomAttributeData> attributes, string fullName)
        {
            foreach (CustomAttributeData attribute in attributes)
            {
                if (attribute.AttributeType.FullName != fullName || attribute.ConstructorArguments.Count != 1)
                    continue;

                object? value = attribute.ConstructorArguments[0].Value;
                if (value is byte flag)
                    return flag;

                // Generic and array shapes pass byte[]; the first entry is the top-level annotation.
                if (value is IList<CustomAttributeTypedArgument> flags && flags.Count > 0 && flags[0].Value is byte first)
                    return first;
            }

            return null;
        }
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

        public static ReadOnlyCollection<T> AsReadOnly<T>(this IList<T> list) => new ReadOnlyCollection<T>(list);

        public static bool TryDequeue<T>(this Queue<T> queue, out T result)
        {
            if (queue.Count == 0)
            {
                result = default!;
                return false;
            }
            result = queue.Dequeue();
            return true;
        }

        public static void Kill(this Process proc, bool entireProcessTree)
        {
            
#if NET48
                    proc.Kill();
#else
                    proc.Kill(entireProcessTree);
#endif
        }

    }

}

namespace System.Runtime.CompilerServices
{

    internal static class IsExternalInit { }

}

namespace System.Diagnostics.CodeAnalysis
{

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }

}

#endif
