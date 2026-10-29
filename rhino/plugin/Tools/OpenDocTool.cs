using Rhino.Display;
using Rhino.DocObjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class OpenDocTool
{
    [McpServerTool("open_doc", "Open / Import Document", false, true)]
    [Description("Import a .3dm (or other supported) file into the current document. Headless — no dialogs. Optionally clear the document first to make this behave like an open-in-place.")]
    public static IToolResult OpenDoc(
        RhinoDoc doc,
        [Description("Absolute path to the file to import")] string path,
        [Description("If true, delete all objects in the current document before importing")] bool clearFirst = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Failure(ToolError.BadArgument, "path is required.");

        if (!System.IO.File.Exists(path))
            return Failure(ToolError.RH_File_NotFound, $"File not found: {path}");

        List<RhinoObject> removables = new(doc.Objects.Count);
        if (clearFirst)
        {
            foreach (RhinoObject? obj in doc.Objects)
            {
                if (obj is null) continue;
                if (!obj.IsDeletable) continue;
                removables.Add(obj);
            }
        }

        int before = doc.Objects.Count - removables.Count;
        if (!doc.Import(path))
            return Failure(ToolError.Failed, $"Failed to import: {path}");

        foreach(RhinoObject obj in removables)
        {
            doc.Objects.Delete(obj.Id, true);
        }

        // TODO : Non 3dm files will offer options and so get stuck!
        // RhinoApp.RunScript(doc.RuntimeSerialNumber, "!_E nter", false);

        int imported = doc.Objects.Count - before;

        foreach (RhinoView? view in doc.Views)
        {
            if (view is null) continue;
            view.ActiveViewport?.ZoomExtents();
        }

        doc.Views.Redraw();

        return Success(new
        {
            path,
            imported,
            cleared = removables.Count,
        });
    }
}
