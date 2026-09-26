using System;
using System.Text;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Rhino.AI.Secrets;

/// <summary>Stores secrets as generic credentials in the Windows Credential Manager.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CredentialVault(string service) : ISecretVault
{
    private const string AdvApi = "advapi32.dll";
    private const uint GenericType = 1;
    private const uint PersistLocalMachine = 2;
    private const int MaxBlobSize = 2560;

    private string Service { get; } = service;

    public bool TryGet(string key, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        if (!CredRead(Target(key), GenericType, 0, out IntPtr handle)) return false;

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(handle);
            byte[] blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            secret = Encoding.UTF8.GetString(blob);
            return true;
        }
        finally
        {
            CredFree(handle);
        }
    }

    public bool TrySet(string key, string secret)
    {
        byte[] blob = Encoding.UTF8.GetBytes(secret);
        if (blob.Length > MaxBlobSize) return false;

        GCHandle pinned = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            Credential credential = new()
            {
                Type = GenericType,
                TargetName = Target(key),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = pinned.AddrOfPinnedObject(),
                Persist = PersistLocalMachine,
                UserName = key,
            };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            pinned.Free();
        }
    }

    public bool TryDelete(string key) => CredDelete(Target(key), GenericType, 0);

    private string Target(string key) => $"{Service}/{key}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport(AdvApi, EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport(AdvApi, EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport(AdvApi, EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport(AdvApi)]
    private static extern void CredFree(IntPtr buffer);
}
