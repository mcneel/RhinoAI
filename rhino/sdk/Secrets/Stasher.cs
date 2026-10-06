using System;
using System.Diagnostics.CodeAnalysis;

namespace Rhino.AI.Secrets;

/// <summary>Stashes secrets such as API keys in the OS vault (Keychain on macOS, Credential Manager on Windows, Secret Service on Linux).</summary>
internal static class Stasher
{
    private const string Service = "Rhino.AI";

    private static ISecretVault? Vault { get; } =
        OperatingSystem.IsWindows() ? new CredentialVault(Service)
        : OperatingSystem.IsMacOS() ? new KeychainVault(Service)
        : OperatingSystem.IsLinux() ? SecretServiceVault.TryCreate(Service)
        : null;

    public static bool IsAvailable => Vault is not null;

    public static bool TryGetSecret(string key, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        if (string.IsNullOrEmpty(key)) return false;
        if (Vault is null) return false;

        return Vault.TryGet(key, out secret);
    }

    public static string? GetSecret(string key)
        => TryGetSecret(key, out string? secret) ? secret : null;

    public static bool TrySetSecret(string key, string secret)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (string.IsNullOrEmpty(secret)) return false;
        if (Vault is null) return false;

        return Vault.TrySet(key, secret);
    }

    public static bool TryDeleteSecret(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (Vault is null) return false;

        return Vault.TryDelete(key);
    }
}
