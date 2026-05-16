using Rhino.FileIO;

namespace RhMcp.Tools;

[McpServerToolType]
public static class CloseDocTool
{
    [McpServerTool(Name = "close_doc")]
    [Description("Close the current Rhino document. If path is given, save to that .3dm path first; otherwise discard unsaved changes.")]
    public static string CloseDoc(
        RhinoDoc doc,
        [Description("Optional absolute .3dm path to save to before closing. Omit to close without saving.")] string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                throw new System.IO.DirectoryNotFoundException($"Directory does not exist: {dir}");

            var options = new FileWriteOptions
            {
                SuppressDialogBoxes = true,
                WriteUserData = true,
                UpdateDocumentPath = true,
            };

            if (!doc.WriteFile(path, options))
                throw new InvalidOperationException($"Failed to save: {path}");
        }

        doc.Modified = false;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, "_-Close", false);
        return path is null ? "Document closed without saving." : $"Document saved to {path} and closed.";
    }
}
