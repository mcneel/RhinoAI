namespace RhMcp.Internal;

// Router-private control tools. The router talks to these over the same MCP
// HTTP endpoint as a control channel; they are intentionally kept out of
// /plugin/Tools/ so the router's source generator (which scans that folder)
// cannot turn them into agent-facing proxies.
//
// Used on macOS, where Rhino is single-process-per-bundle-id: multiple "slots"
// for the same Rhino version share one OS process, and each slot is a separate
// RhinoDoc with its own MCP listener. The first slot is launched via `open -a`
// with `_-RhinoMCP <port> _Enter`; subsequent slots are created by calling
// SpawnListener on the existing first listener.
[McpServerToolType]
public static class RouterControlTool
{
    [McpServerTool(Name = "_router_spawn_listener")]
    [Description("Router-internal: create a new RhinoDoc and start an MCP listener bound to it. Returns { port }.")]
    public static string SpawnListener()
    {
        RhinoDoc? newDoc = null;
        int port = 0;
        string? error = null;

        RhinoApp.InvokeAndWait(() =>
        {
            try
            {
                var seen = RhinoDoc.OpenDocuments()
                    .Select(d => d.RuntimeSerialNumber)
                    .ToHashSet();

                // `_New` (no dash). The dash-form `_-New` creates a *headless* doc with
                // no viewport/UI plumbing, which would break this whole flow.
                RhinoApp.RunScript("_New", false);

                // ActiveDoc is focus-driven and unreliable across UI events — diff the
                // open-doc set instead to identify the doc we just created.
                newDoc = RhinoDoc.OpenDocuments()
                    .Where(d => !seen.Contains(d.RuntimeSerialNumber))
                    .OrderByDescending(d => d.RuntimeSerialNumber)
                    .FirstOrDefault();

                if (newDoc is null)
                {
                    error = "_New ran but no new RhinoDoc appeared.";
                    return;
                }

                port = RhinoMcpHost.GetNextPort();
                if (!RhinoMcpHost.Start(newDoc, port))
                {
                    error = $"Failed to start MCP listener on port {port} for new doc.";
                }
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
        });

        if (error is not null)
            throw new InvalidOperationException(error);

        return JsonSerializer.Serialize(new { port });
    }

    [McpServerTool(Name = "_router_close_listener")]
    [Description("Router-internal: stop the MCP listener on the given port and close its associated doc without saving.")]
    public static string CloseListener(int port)
    {
        bool ok = false;
        RhinoApp.InvokeAndWait(() => { ok = RhinoMcpHost.StopByPort(port); });
        return JsonSerializer.Serialize(new { closed = ok });
    }
}
