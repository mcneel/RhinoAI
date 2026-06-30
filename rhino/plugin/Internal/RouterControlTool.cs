using System.IO;
using System.Threading.Tasks;

using Rhino.FileIO;

namespace RhMcp.Internal;

// Router-private control tools. The router talks to these over the same MCP
// HTTP endpoint as a control channel; they are intentionally kept out of
// /plugin/Tools/ so the router's source generator (which scans that folder)
// cannot turn them into agent-facing proxies.
[McpServerToolType]
public static class RouterControlTool
{
    [McpServerTool("_router_spawn_listener")]
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
                HashSet<uint> seen = RhinoDoc.OpenDocuments()
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

                if (!RhinoMcpHost.TryGetNextPort(out port))
                {
                    error = "No free port available for new doc; could not start MCP listener.";
                    DiscardDoc(newDoc);
                    return;
                }
                if (!RhinoMcpHost.Start(newDoc, port))
                {
                    error = $"Failed to start MCP listener on port {port} for new doc.";
                    DiscardDoc(newDoc);
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

    // Close a freshly-`_New`'d doc we couldn't attach a listener to, so a port or
    // start failure doesn't leave it orphaned. Must run on the UI thread (callers
    // are inside InvokeAndWait). Mirrors RhinoMcpHost.StopByPort/CloseDocTool: Mac's
    // `_-Close` resolves the doc by on-disk path, so give the (unmodified) doc a temp
    // path via WriteFile, close it, then sweep the temp file after Cocoa's deferred
    // close.
    private static void DiscardDoc(RhinoDoc doc)
    {
        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"rh-mcp-spawn-discard-{doc.RuntimeSerialNumber}-{Guid.NewGuid():N}.3dm");
        try
        {
            doc.Modified = false;
            doc.WriteFile(tempPath, new FileWriteOptions
            {
                SuppressDialogBoxes = true,
                WriteUserData = true,
                UpdateDocumentPath = true,
            });
            RhinoApp.RunScript(doc.RuntimeSerialNumber, $"_-Close \"{tempPath}\"", false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to discard orphaned spawn doc: {ex.Message}");
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            try
            { File.Delete(tempPath); }
            catch { /* OS temp sweep will get it */ }
        });
    }

    [McpServerTool("_router_close_listener")]
    [Description("Router-internal: stop the MCP listener on the given port and close its associated doc without saving.")]
    public static string CloseListener(int port)
    {
        bool ok = false;
        RhinoApp.InvokeAndWait(() => { ok = RhinoMcpHost.StopByPort(port); });
        return JsonSerializer.Serialize(new { closed = ok });
    }

    // _Exit shows a save-changes dialog for modified docs, which would deadlock
    // the router waiting for a process exit that never happens. Clear Modified
    // on every doc first, then fire _Exit on a delayed background task so this
    // HTTP response can unwind before Rhino starts tearing itself down.
    [McpServerTool("_router_quit_app")]
    [Description("Router-internal: schedule a graceful Rhino exit via _Exit. Returns immediately; the actual quit fires shortly after on the UI thread.")]
    public static string QuitApp()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200).ConfigureAwait(false);
                RhinoApp.InvokeAndWait(() =>
                {
                    foreach (RhinoDoc doc in RhinoDoc.OpenDocuments(true))
                        doc.Modified = false;
                    RhinoApp.RunScript("_Exit", false);
                });
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Rhino MCP] _Exit dispatch failed: {ex.Message}");
            }
        });
        return JsonSerializer.Serialize(new { scheduled = true });
    }
}
