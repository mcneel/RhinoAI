using System.Reflection;
using System.Runtime.InteropServices;

using Rhino.Runtime;

namespace RhMcp.Resources;

[McpServerResourceType]
public static class HostEnvironmentResource
{
    [McpServerResource(
        UriTemplate = "rhino://host/environment",
        Name = "host_environment",
        MimeType = "application/json")]
    [Description(
        "Ambient context about the running Rhino process: Rhino version, OS, " +
        "and which host application Rhino is embedded in (standalone, " +
        "Rhino.Inside Revit, Rhino.Inside AutoCAD, Rhino Compute, etc.). " +
        "Read this before deciding whether host-specific tools (e.g. Revit) " +
        "are applicable.")]
    public static string Read()
    {
        var info = Collect();
        return JsonSerializer.Serialize(info, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    private static HostInfo Collect()
    {
        var (kind, host) = DetectHost();

        return new HostInfo
        {
            Rhino = new RhinoInfo
            {
                Version = RhinoApp.Version.ToString(),
                BuildDate = RhinoApp.BuildDate.ToString("yyyy-MM-dd"),
                IsRhinoInside = HostUtils.RunningAsRhinoInside,
            },
            Runtime = new RuntimeInfo
            {
                Os = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                DotNet = RuntimeInformation.FrameworkDescription,
            },
            HostKind = kind,
            Host = host,
        };
    }

    private static (string kind, HostApplicationInfo? info) DetectHost()
    {
        if (!HostUtils.RunningAsRhinoInside)
            return ("standalone", null);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name ?? string.Empty;
            if (name.Equals("RevitAPI", StringComparison.OrdinalIgnoreCase))
                return ("rhino_inside_revit", BuildRevitInfo());
            if (name.Equals("acmgd", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AcMgd", StringComparison.OrdinalIgnoreCase))
                return ("rhino_inside_autocad", new HostApplicationInfo { Name = "AutoCAD" });
        }

        return ("rhino_inside_unknown", null);
    }

    private static HostApplicationInfo? BuildRevitInfo()
    {
        try
        {
            var revitApi = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "RevitAPI", StringComparison.OrdinalIgnoreCase));
            if (revitApi is null) return new HostApplicationInfo { Name = "Revit" };

            var appType = revitApi.GetType("Autodesk.Revit.ApplicationServices.Application");
            var version = revitApi.GetName().Version?.ToString();

            // Try to read the live Application instance from RhinoInside.Revit if present.
            string? versionNumber = null;
            string? versionName = null;
            string? language = null;

            var rir = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => (a.GetName().Name ?? "").StartsWith("RhinoInside.Revit", StringComparison.OrdinalIgnoreCase));
            if (rir is not null && appType is not null)
            {
                var revitType = rir.GetType("RhinoInside.Revit.Revit");
                var appProp = revitType?.GetProperty("ActiveDBApplication", BindingFlags.Public | BindingFlags.Static)
                              ?? revitType?.GetProperty("ApplicationServices", BindingFlags.Public | BindingFlags.Static);
                var appInstance = appProp?.GetValue(null);
                if (appInstance is not null)
                {
                    versionNumber = appType.GetProperty("VersionNumber")?.GetValue(appInstance) as string;
                    versionName = appType.GetProperty("VersionName")?.GetValue(appInstance) as string;
                    language = appType.GetProperty("Language")?.GetValue(appInstance)?.ToString();
                }
            }

            return new HostApplicationInfo
            {
                Name = "Revit",
                AssemblyVersion = version,
                VersionNumber = versionNumber,
                VersionName = versionName,
                Language = language,
            };
        }
        catch
        {
            return new HostApplicationInfo { Name = "Revit" };
        }
    }

    private sealed class HostInfo
    {
        public RhinoInfo Rhino { get; init; } = new();
        public RuntimeInfo Runtime { get; init; } = new();
        public string HostKind { get; init; } = "standalone";
        public HostApplicationInfo? Host { get; init; }
    }

    private sealed class RhinoInfo
    {
        public string Version { get; init; } = "";
        public string BuildDate { get; init; } = "";
        public bool IsRhinoInside { get; init; }
    }

    private sealed class RuntimeInfo
    {
        public string Os { get; init; } = "";
        public string Architecture { get; init; } = "";
        public string DotNet { get; init; } = "";
    }

    private sealed class HostApplicationInfo
    {
        public string Name { get; init; } = "";
        public string? AssemblyVersion { get; init; }
        public string? VersionNumber { get; init; }
        public string? VersionName { get; init; }
        public string? Language { get; init; }
    }
}
