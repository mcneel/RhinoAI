using System.Linq;

using Rhino.DocObjects;

namespace Rhino.AI.Tools;

// Rhino's own What / Properties report, captured from the command line rather than rebuilt from
// RhinoCommon: the point is that the agent reads exactly what the user would read if they ran the
// command themselves, including the parts of an object's description we would not think to include.
[McpServerToolType]
internal static class ObjectPropertiesTool
{
    [McpServerTool("get_object_properties", "Object Properties (What)", readOnly: true)]
    [Description("Run Rhino's What command over objects and return its report verbatim: type, layer, "
        + "geometry description, and whatever else Rhino says about each one. With no ids it reports on the "
        + "current selection. Use it to find out what something actually is before editing it.")]
    public static IToolResult GetObjectProperties(
        RhinoDoc doc,
        [Description("Object ids to report on; omit to use the current selection")] string[]? objectIds = null)
    {
        RhinoObject[] wanted;
        if (objectIds is { Length: > 0 })
        {
            wanted = Resolve(doc, objectIds, out string[] missing);
            if (missing.Length > 0)
                return Failure(
                    ToolError.NotFound,
                    $"No object with id {string.Join(", ", missing)}.",
                    "Call list_objects or get_selection for ids that exist in this document.");
        }
        else
        {
            wanted = [.. doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false)];
        }

        if (wanted.Length == 0)
            return Failure(
                ToolError.BadArgument,
                "Nothing is selected and no ids were given.",
                "Pass objectIds, or ask the user to select something first.");

        // What reports on the selection, so the selection is the argument. It is put back afterwards
        // because the user's selection is theirs, not a scratch variable.
        Guid[] restore = [.. doc.Objects.GetSelectedObjects(includeLights: true, includeGrips: false).Select(o => o.Id)];
        string report;
        try
        {
            doc.Objects.UnselectAll();
            foreach (RhinoObject obj in wanted)
                doc.Objects.Select(obj.Id);
            report = Capture(doc);
        }
        finally
        {
            doc.Objects.UnselectAll();
            foreach (Guid id in restore)
                doc.Objects.Select(id);
            doc.Views.Redraw();
        }

        return report.Length == 0
            ? Failure(ToolError.Failed, "Rhino's What command reported nothing.")
            : Success(new { count = wanted.Length, report });
    }

    private static RhinoObject[] Resolve(RhinoDoc doc, string[] ids, out string[] missing)
    {
        List<RhinoObject> found = [];
        List<string> absent = [];
        foreach (string id in ids)
        {
            RhinoObject? obj = Guid.TryParse(id, out Guid parsed) ? doc.Objects.FindId(parsed) : null;
            if (obj is null)
                absent.Add(id);
            else
                found.Add(obj);
        }
        missing = [.. absent];
        return [.. found];
    }

    // Capture is application-wide, so it is turned off again whatever happens; leaving it on would
    // quietly accumulate every later command's output for whoever reads it next.
    private static string Capture(RhinoDoc doc)
    {
        bool captured = RhinoApp.CommandWindowCaptureEnabled;
        RhinoApp.CommandWindowCaptureEnabled = true;
        try
        {
            RhinoApp.RunScript(doc.RuntimeSerialNumber, "_What", echo: false);
            return string.Concat(RhinoApp.CapturedCommandWindowStrings(true) ?? []).TrimEnd();
        }
        finally
        {
            RhinoApp.CommandWindowCaptureEnabled = captured;
        }
    }
}
