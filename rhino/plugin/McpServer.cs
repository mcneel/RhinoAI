using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RhMcp.Server;

namespace RhMcp;

internal sealed class McpServer : IDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;

    public bool HasStarted => _app is not null;

    public int Port { get; private set; }

    public bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted) return true;
        Port = port;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new RhinoLoggerProvider());
#if DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Information);
#else
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif
            builder.Services.Configure<KestrelServerOptions>(o => o.ListenLocalhost(port));

            builder.Services.AddSingleton(doc);

            var asm = typeof(McpServer).Assembly;

            _app = builder.Build();

            var endpointOptions = new McpEndpointOptions
            {
                ServerName = "rhino-mcp",
                ServerVersion = "0.1.0",
                ToolAssembly = asm,
#if DEBUG
                SurfaceExceptionDetailsToClient = true,
#endif
            };
            _app.MapMcp("/", endpointOptions);

            _cts = new CancellationTokenSource();
            _ = _app.RunAsync(_cts.Token);

            RhinoApp.WriteLine($"[Rhino MCP] MCP server currently running on http://localhost:{port}/");
            return true;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to start: {DescribeException(ex)}");
            RhinoApp.WriteLine(DumpDiagnostics(ex));
            _app = null;
            return false;
        }
    }

    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            parts.Add($"{cur.GetType().FullName}: {cur.Message}");
        return string.Join(" --> ", parts);
    }

    private static string DumpDiagnostics(Exception root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Rhino MCP] ===== Diagnostics =====");

        try
        {
            sb.AppendLine($"[Rhino MCP] Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier}) on {RuntimeInformation.OSDescription}, ProcessArch={RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"[Rhino MCP] AppContext.BaseDirectory: {AppContext.BaseDirectory}");
            sb.AppendLine($"[Rhino MCP] Plugin assembly: {typeof(McpServer).Assembly.Location}");
        }
        catch (Exception ex) { sb.AppendLine($"[Rhino MCP] (runtime info dump failed: {ex.Message})"); }

        sb.AppendLine("[Rhino MCP] -- Exception chain --");
        var depth = 0;
        for (var cur = root; cur is not null; cur = cur.InnerException, depth++)
        {
            sb.AppendLine($"[Rhino MCP] [{depth}] {cur.GetType().FullName}: {cur.Message}");
            switch (cur)
            {
                case TypeLoadException tle:
                    sb.AppendLine($"[Rhino MCP]     TypeName: {tle.TypeName}");
                    break;
                case FileNotFoundException fnf:
                    sb.AppendLine($"[Rhino MCP]     FileName: {fnf.FileName}");
                    if (!string.IsNullOrEmpty(fnf.FusionLog)) sb.AppendLine($"[Rhino MCP]     FusionLog: {fnf.FusionLog}");
                    break;
                case FileLoadException fle:
                    sb.AppendLine($"[Rhino MCP]     FileName: {fle.FileName}");
                    if (!string.IsNullOrEmpty(fle.FusionLog)) sb.AppendLine($"[Rhino MCP]     FusionLog: {fle.FusionLog}");
                    break;
                case BadImageFormatException bif:
                    sb.AppendLine($"[Rhino MCP]     FileName: {bif.FileName}");
                    break;
                case ReflectionTypeLoadException rtle:
                    sb.AppendLine($"[Rhino MCP]     LoaderExceptions ({rtle.LoaderExceptions?.Length ?? 0}):");
                    foreach (var le in rtle.LoaderExceptions ?? Array.Empty<Exception?>())
                        sb.AppendLine($"[Rhino MCP]       - {le?.GetType().FullName}: {le?.Message}");
                    break;
            }
            if (!string.IsNullOrEmpty(cur.StackTrace))
            {
                foreach (var line in cur.StackTrace.Split('\n'))
                    sb.AppendLine($"[Rhino MCP]     {line.TrimEnd('\r')}");
            }
        }

        sb.AppendLine("[Rhino MCP] -- Loaded assemblies of interest --");
        var prefixes = new[]
        {
            "System.Text.Json",
            "System.Text.Encodings.Web",
            "Microsoft.Extensions.AI",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Options",
            "Microsoft.AspNetCore",
            "ModelContextProtocol",
            "System.IO.Pipelines",
            "System.Net.ServerSentEvents",
        };
        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => prefixes.Any(p => (a.GetName().Name ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.GetName().Version);
            foreach (var asm in loaded)
            {
                var n = asm.GetName();
                var loc = SafeLocation(asm);
                sb.AppendLine($"[Rhino MCP]     {n.Name} v{n.Version} | ALC={AssemblyLoadContext.GetLoadContext(asm)?.Name ?? "(null)"} | {loc}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"[Rhino MCP] (assembly enumeration failed: {ex.Message})"); }

        sb.AppendLine("[Rhino MCP] -- System.Text.Json type probe --");
        ProbeType(sb, "System.Text.Json.JsonElement, System.Text.Json");
        ProbeType(sb, "System.Text.Json.Schema.JsonSchemaExporter, System.Text.Json");
        ProbeType(sb, "System.Text.Json.Schema.JsonSchemaExporterContext, System.Text.Json");

        sb.AppendLine("[Rhino MCP] -- Plugin-bin candidate assemblies --");
        try
        {
            var dir = Path.GetDirectoryName(typeof(McpServer).Assembly.Location);
            if (dir is not null)
            {
                foreach (var p in prefixes)
                {
                    var path = Path.Combine(dir, p + ".dll");
                    if (File.Exists(path))
                    {
                        try
                        {
                            var an = AssemblyName.GetAssemblyName(path);
                            sb.AppendLine($"[Rhino MCP]     {an.Name} v{an.Version} | {path}");
                        }
                        catch (Exception ex) { sb.AppendLine($"[Rhino MCP]     {path} (read failed: {ex.Message})"); }
                    }
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"[Rhino MCP] (bin scan failed: {ex.Message})"); }

        sb.AppendLine("[Rhino MCP] ===== End diagnostics =====");
        return sb.ToString();
    }

    private static void ProbeType(StringBuilder sb, string assemblyQualifiedName)
    {
        try
        {
            var t = Type.GetType(assemblyQualifiedName, throwOnError: false);
            if (t is null)
            {
                sb.AppendLine($"[Rhino MCP]     {assemblyQualifiedName} -> not found");
                return;
            }
            sb.AppendLine($"[Rhino MCP]     {assemblyQualifiedName} -> {t.Assembly.GetName().FullName}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Rhino MCP]     {assemblyQualifiedName} -> {ex.GetType().FullName}: {ex.Message}");
        }
    }

    private static string SafeLocation(Assembly a)
    {
        try { return string.IsNullOrEmpty(a.Location) ? "(no location)" : a.Location; }
        catch (Exception ex) { return $"(location error: {ex.Message})"; }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _app?.StopAsync(); } catch { }
        _app = null;
    }

    public void Dispose() => Stop();
}
