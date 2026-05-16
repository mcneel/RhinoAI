using Rhino.FileIO;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SaveDocTool
{
    [McpServerTool(Name = "save_doc")]
    [Description("Write the current document to the given .3dm path. Headless — no dialogs. Overwrites any existing file at the path.")]
    public static string SaveDoc(
        RhinoDoc doc,
        [Description("Absolute path to write the .3dm file to")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required.", nameof(path));

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            throw new System.IO.DirectoryNotFoundException($"Directory does not exist: {dir}");

        // UpdateDocumentPath=false: avoids the post-write LockDocument that pops a modal in R9.
        var options = new FileWriteOptions
        {
            SuppressDialogBoxes = true,
            WriteUserData = true,
            UpdateDocumentPath = false,
        };

        if (!doc.WriteFile(path, options))
            throw new InvalidOperationException($"Failed to save: {path}");

        return JsonSerializer.Serialize(new
        {
            path,
            objects = doc.Objects.Count,
        });
    }
}
