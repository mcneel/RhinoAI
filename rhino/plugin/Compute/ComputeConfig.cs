using Rhino.Compute;

namespace RhMcp.Compute;

// Centralizes Rhino Compute client configuration. The SDK in this folder
// (RhinoCompute.cs) is copied verbatim from compute.rhino3d and configures
// itself via the mutable static
// ComputeServer.{WebAddress,AuthToken,ApiKey}. We read those once from env
// vars the first time a compute tool touches this type so the agent doesn't
// have to pass a server URL on every call. The CLR guarantees the static
// constructor runs exactly once, lazily, on first reference — so a session
// that never uses compute never reads compute env vars or pings a server.
//
//   RHINO_COMPUTE_URL      e.g. http://localhost:6500 (default if unset)
//   RHINO_COMPUTE_API_KEY  for self-hosted compute (sent as RhinoComputeKey)
//   RHINO_COMPUTE_AUTH     bearer token for compute.rhino3d.com only
internal static class ComputeConfig
{
    public const string DefaultUrl = "http://localhost:6500";

    public static bool HasCustomUrl { get; }

    static ComputeConfig()
    {
        var url = Environment.GetEnvironmentVariable("RHINO_COMPUTE_URL");
        HasCustomUrl = !string.IsNullOrWhiteSpace(url);
        ComputeServer.WebAddress = HasCustomUrl ? url!.Trim() : DefaultUrl;
        ComputeServer.ApiKey = Environment.GetEnvironmentVariable("RHINO_COMPUTE_API_KEY") ?? string.Empty;
        ComputeServer.AuthToken = Environment.GetEnvironmentVariable("RHINO_COMPUTE_AUTH") ?? string.Empty;
    }

    // Reference any member to force the cctor; tools call this on entry so the
    // server URL / keys are populated before the first SDK call.
    public static void EnsureInitialized() { }

    public static string CurrentUrl => ComputeServer.WebAddress;
}
