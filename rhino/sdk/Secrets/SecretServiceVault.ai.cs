using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace Rhino.AI.Secrets;

[SupportedOSPlatform("linux")]
internal sealed class SecretServiceVault : ISecretVault
{
    private const string SecretLib = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string ServiceAttribute = "service";
    private const string AccountAttribute = "account";

    // SecretSchema is a name, flags, then 32 (name, type) attribute slots and reserved padding, all zero-terminated.
    private const int SchemaSize = 8 + 8 + (32 * 16) + 8 + (7 * 8);

    private SecretServiceVault(string service, IntPtr strHash, IntPtr strEqual)
    {
        Service = service;
        StrHash = strHash;
        StrEqual = strEqual;
        Schema = CreateSchema(service);
    }

    private string Service { get; }

    private IntPtr StrHash { get; }

    private IntPtr StrEqual { get; }

    private IntPtr Schema { get; }

    public static SecretServiceVault? TryCreate(string service)
    {
        if (!NativeLibrary.TryLoad(SecretLib, out _)) return null;
        if (!NativeLibrary.TryLoad(GLib, out IntPtr glib)) return null;

        return new SecretServiceVault(service, NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
    }

    public bool TryGet(string key, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        List<IntPtr> strings = [];
        IntPtr attributes = Attributes(strings, key);
        try
        {
            IntPtr password = secret_password_lookupv_sync(Schema, attributes, IntPtr.Zero, out IntPtr error);
            if (Failed(error) || password == IntPtr.Zero) return false;

            secret = Marshal.PtrToStringUTF8(password);
            secret_password_free(password);
            return secret is not null;
        }
        finally
        {
            Release(attributes, strings);
        }
    }

    public bool TrySet(string key, string secret)
    {
        List<IntPtr> strings = [];
        IntPtr attributes = Attributes(strings, key);
        try
        {
            bool stored = secret_password_storev_sync(Schema, attributes, null, $"{Service} {key}", secret, IntPtr.Zero, out IntPtr error);
            return !Failed(error) && stored;
        }
        finally
        {
            Release(attributes, strings);
        }
    }

    public bool TryDelete(string key)
    {
        List<IntPtr> strings = [];
        IntPtr attributes = Attributes(strings, key);
        try
        {
            bool cleared = secret_password_clearv_sync(Schema, attributes, IntPtr.Zero, out IntPtr error);
            return !Failed(error) && cleared;
        }
        finally
        {
            Release(attributes, strings);
        }
    }

    private IntPtr Attributes(List<IntPtr> strings, string key)
    {
        IntPtr table = g_hash_table_new(StrHash, StrEqual);
        g_hash_table_insert(table, Utf8(strings, ServiceAttribute), Utf8(strings, Service));
        g_hash_table_insert(table, Utf8(strings, AccountAttribute), Utf8(strings, key));
        return table;
    }

    private static IntPtr Utf8(List<IntPtr> strings, string value)
    {
        IntPtr pointer = Marshal.StringToCoTaskMemUTF8(value);
        strings.Add(pointer);
        return pointer;
    }

    private static void Release(IntPtr table, List<IntPtr> strings)
    {
        g_hash_table_unref(table);
        foreach (IntPtr pointer in strings)
            Marshal.FreeCoTaskMem(pointer);
    }

    private static bool Failed(IntPtr error)
    {
        if (error == IntPtr.Zero) return false;
        g_error_free(error);
        return true;
    }

    private static IntPtr CreateSchema(string name)
    {
        IntPtr schema = Marshal.AllocHGlobal(SchemaSize);
        for (int offset = 0; offset < SchemaSize; offset += 8)
            Marshal.WriteInt64(schema, offset, 0);

        Marshal.WriteIntPtr(schema, 0, Marshal.StringToHGlobalAnsi(name));
        Marshal.WriteIntPtr(schema, 16, Marshal.StringToHGlobalAnsi(ServiceAttribute));
        Marshal.WriteIntPtr(schema, 32, Marshal.StringToHGlobalAnsi(AccountAttribute));
        return schema;
    }

    [DllImport(SecretLib)]
    private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [DllImport(SecretLib)]
    private static extern bool secret_password_storev_sync(
        IntPtr schema,
        IntPtr attributes,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
        IntPtr cancellable,
        out IntPtr error);

    [DllImport(SecretLib)]
    private static extern bool secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [DllImport(SecretLib)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport(GLib)]
    private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

    [DllImport(GLib)]
    private static extern bool g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLib)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLib)]
    private static extern void g_error_free(IntPtr error);
}
