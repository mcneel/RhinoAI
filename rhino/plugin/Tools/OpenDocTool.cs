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
        [Description("If true, delete all objects and groups in the current document before importing, so the imported objects keep the ids they have in the file. Clears the undo history.")] bool clearFirst = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Failure(ToolError.BadArgument, "path is required.");

        if (!System.IO.File.Exists(path))
            return Failure(ToolError.RH_File_NotFound, $"File not found: {path}");

        ObjectEnumeratorSettings all = new()
        {
            NormalObjects = true,
            LockedObjects = true,
            HiddenObjects = true,
            IncludeLights = true,
        };

        List<RhinoObject> removed = new();
        List<int> removedGroups = new();
        if (clearFirst)
        {
            // Import gives an incoming object a new id when a live object already has that
            // id, and renames an incoming group when its name is taken. So the old content
            // goes before the import, not after it.
            List<RhinoObject> old = new(doc.Objects.Count);
            foreach (RhinoObject? obj in doc.Objects.GetObjectList(all))
            {
                if (obj is null) continue;
                old.Add(obj);
            }

            foreach (RhinoObject obj in old)
            {
                if (doc.Objects.Delete(obj, true, true)) removed.Add(obj);
            }

            // Deleting its objects leaves a group in the table, empty.
            for (int i = 0; i < doc.Groups.Count; i++)
            {
                if (doc.Groups.IsDeleted(i)) continue;
                if (doc.Groups.Delete(i)) removedGroups.Add(i);
            }
        }

        // doc.Objects.Count still counts deleted objects, so it cannot tell what was imported.
        int before = doc.Objects.ObjectCount(all);
        if (!doc.Import(path))
        {
            foreach (RhinoObject obj in removed) doc.Objects.Undelete(obj);
            foreach (int i in removedGroups) doc.Groups.Undelete(i);
            return Failure(ToolError.Failed, $"Failed to import: {path}");
        }

        // An undo step recorded before the clear would bring old objects back beside the new.
        if (clearFirst) doc.ClearUndoRecords(true);

        // TODO : Non 3dm files will offer options and so get stuck!
        // RhinoApp.RunScript(doc.RuntimeSerialNumber, "!_E nter", false);

        int imported = doc.Objects.ObjectCount(all) - before;

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
            cleared = removed.Count,
        });
    }
}
