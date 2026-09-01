using RhMcp.Compute;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ComputeStatusTool
{
    [McpServerTool(Name = "compute_status")]
    [BackgroundThread]
    [Description("Check whether the Rhino Compute server is reachable and report setup hints. Returns JSON with url, reachable, version, platform, isCustomUrl, hopsInstalled, and hints (an array of actionable next steps when unreachable). Use this to verify setup before solving, or after compute_grasshopper / compute_python report a connection error.")]
    public static string ComputeStatus()
    {
        var status = ComputeDiagnostics.Probe();
        return JsonSerializer.Serialize(ComputeDiagnostics.ToDto(status));
    }
}
