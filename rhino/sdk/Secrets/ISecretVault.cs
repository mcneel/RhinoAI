using System.Diagnostics.CodeAnalysis;

namespace Rhino.AI.Secrets;

internal interface ISecretVault
{
    bool TryGet(string key, [NotNullWhen(true)] out string? secret);

    bool TrySet(string key, string secret);

    bool TryDelete(string key);
}
