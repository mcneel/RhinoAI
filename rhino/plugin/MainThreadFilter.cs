using System.Reflection;
using System.Threading.Tasks;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RhMcp;

// Every MCP tool call is dispatched onto the Rhino UI thread by default so
// tool bodies can freely touch Rhino/Grasshopper without crashing macOS's
// AppKit thread check. Tools that explicitly opt out via [BackgroundThread]
// run on whatever worker thread the MCP host gave us.
//
// The marshal is "proper": we post via InvokeOnUiThread (non-blocking) and
// resume on a TCS, instead of blocking a worker on .GetResult() — which
// would deadlock the moment a tool body genuinely awaits.
internal static class MainThreadFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(IReadOnlySet<string> optOut) =>
        next => async (request, ct) =>
        {
            var name = request.Params?.Name;
            if (name is not null && optOut.Contains(name))
                return await next(request, ct);

            var tcs = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread(new Action(async () =>
            {
                try { tcs.SetResult(await next(request, ct)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }), null);
            return await tcs.Task;
        };

    public static HashSet<string> ScanOptOut(Assembly asm)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in asm.GetTypes())
        {
            if (t.GetCustomAttribute<McpServerToolTypeAttribute>() is null) continue;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMethods(flags))
            {
                var tool = m.GetCustomAttribute<McpServerToolAttribute>();
                if (tool is null) continue;
                if (m.GetCustomAttribute<BackgroundThreadAttribute>() is null) continue;
                set.Add(tool.Name ?? m.Name);
            }
        }
        return set;
    }
}
