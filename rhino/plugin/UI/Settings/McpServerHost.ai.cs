using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal abstract record McpServerStatus
{
    private McpServerStatus() { }

    public sealed record NoDocument : McpServerStatus;
    public sealed record Stopped(string Document) : McpServerStatus;
    public sealed record Running(string Document, int Port) : McpServerStatus;
}

internal abstract record HostProblem
{
    private HostProblem() { }

    public sealed record NoDocument : HostProblem;
    public sealed record NoFreePort : HostProblem;
    public sealed record StartFailed(int Port) : HostProblem;
}

internal static class McpServerHost
{
    public static McpServerStatus Status()
    {
        if (RhinoDoc.ActiveDoc is not RhinoDoc doc)
            return new McpServerStatus.NoDocument();

        string title = string.IsNullOrEmpty(doc.Name) ? LOC.STR("Untitled") : doc.Name;
        return RhinoAIHost.TryGetPortFor(doc, out int port)
            ? new McpServerStatus.Running(title, port)
            : new McpServerStatus.Stopped(title);
    }

    public static HostProblem? Toggle()
    {
        if (RhinoDoc.ActiveDoc is not RhinoDoc doc)
            return new HostProblem.NoDocument();

        if (RhinoAIHost.TryGetPortFor(doc, out _))
        {
            RhinoAIHost.Stop(doc);
            return null;
        }

        if (!RhinoAIHost.TryGetNextPort(out int port))
            return new HostProblem.NoFreePort();

        return RhinoAIHost.StartOrRestart(doc, port, quiet: true) ? null : new HostProblem.StartFailed(port);
    }
}
