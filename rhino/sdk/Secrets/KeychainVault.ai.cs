using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace Rhino.AI.Secrets;

[SupportedOSPlatform("macos")]
internal sealed class KeychainVault(string service) : ISecretVault
{
    private const string SecurityPath = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int Success = 0;
    private const int ItemNotFound = -25300;

    private static IntPtr Security { get; } = NativeLibrary.Load(SecurityPath);
    private static IntPtr CoreFoundation { get; } = NativeLibrary.Load(CoreFoundationPath);

    private static IntPtr Class { get; } = Constant(Security, "kSecClass");
    private static IntPtr GenericPassword { get; } = Constant(Security, "kSecClassGenericPassword");
    private static IntPtr AttrService { get; } = Constant(Security, "kSecAttrService");
    private static IntPtr AttrAccount { get; } = Constant(Security, "kSecAttrAccount");
    private static IntPtr ValueData { get; } = Constant(Security, "kSecValueData");
    private static IntPtr ReturnData { get; } = Constant(Security, "kSecReturnData");
    private static IntPtr MatchLimit { get; } = Constant(Security, "kSecMatchLimit");
    private static IntPtr MatchLimitOne { get; } = Constant(Security, "kSecMatchLimitOne");
    private static IntPtr True { get; } = Constant(CoreFoundation, "kCFBooleanTrue");
    private static IntPtr KeyCallBacks { get; } = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryKeyCallBacks");
    private static IntPtr ValueCallBacks { get; } = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryValueCallBacks");

    private string Service { get; } = service;

    public bool TryGet(string key, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        List<IntPtr> owned = [];
        try
        {
            IntPtr query = Dictionary(owned, [.. Identity(owned, key), (ReturnData, True), (MatchLimit, MatchLimitOne)]);
            if (SecItemCopyMatching(query, out IntPtr data) != Success) return false;
            owned.Add(data);

            byte[] bytes = new byte[CFDataGetLength(data)];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
            secret = Encoding.UTF8.GetString(bytes);
            return true;
        }
        finally
        {
            Release(owned);
        }
    }

    public bool TrySet(string key, string secret)
    {
        List<IntPtr> owned = [];
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(secret);
            IntPtr data = Own(owned, CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

            int status = SecItemUpdate(Dictionary(owned, Identity(owned, key)), Dictionary(owned, [(ValueData, data)]));
            if (status == ItemNotFound)
                status = SecItemAdd(Dictionary(owned, [.. Identity(owned, key), (ValueData, data)]), IntPtr.Zero);

            return status == Success;
        }
        finally
        {
            Release(owned);
        }
    }

    public bool TryDelete(string key)
    {
        List<IntPtr> owned = [];
        try
        {
            return SecItemDelete(Dictionary(owned, Identity(owned, key))) == Success;
        }
        finally
        {
            Release(owned);
        }
    }

    private (IntPtr Key, IntPtr Value)[] Identity(List<IntPtr> owned, string key) =>
    [
        (Class, GenericPassword),
        (AttrService, String(owned, Service)),
        (AttrAccount, String(owned, key)),
    ];

    private static IntPtr String(List<IntPtr> owned, string value) =>
        Own(owned, CFStringCreateWithCharacters(IntPtr.Zero, value, value.Length));

    private static IntPtr Dictionary(List<IntPtr> owned, (IntPtr Key, IntPtr Value)[] entries)
    {
        IntPtr[] keys = new IntPtr[entries.Length];
        IntPtr[] values = new IntPtr[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            (keys[i], values[i]) = entries[i];

        return Own(owned, CFDictionaryCreate(IntPtr.Zero, keys, values, entries.Length, KeyCallBacks, ValueCallBacks));
    }

    private static IntPtr Own(List<IntPtr> owned, IntPtr reference)
    {
        owned.Add(reference);
        return reference;
    }

    private static void Release(List<IntPtr> owned)
    {
        foreach (IntPtr reference in owned)
            if (reference != IntPtr.Zero)
                CFRelease(reference);
    }

    // The exported symbols are pointers to the constant CFStringRefs, not the strings themselves.
    private static IntPtr Constant(IntPtr library, string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    [DllImport(SecurityPath)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityPath)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityPath)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityPath)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(CoreFoundationPath)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, [MarshalAs(UnmanagedType.LPWStr)] string chars, nint length);

    [DllImport(CoreFoundationPath)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationPath)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationPath)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(IntPtr reference);
}
