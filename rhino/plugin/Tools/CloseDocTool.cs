using System.IO;
using System.Threading.Tasks;

using Rhino.FileIO;

namespace RhMcp.Tools;

// TODO : Close doc should not spawn a rhino to close it
[McpServerToolType]
public static class CloseDocTool
{
    [McpServerTool("close_doc", "Close Document", false, true)]
    [Description("Close the current Rhino document. If path is given, save to that .3dm path first; otherwise discard unsaved changes.")]
    public static string CloseDoc(
        RhinoDoc doc,
        [Description("Optional absolute .3dm path to save to before closing. Omit to close without saving.")] string? path = null)
    {
        bool hasPath = !string.IsNullOrWhiteSpace(path);

        // No-save case: Mac's `_-Close` matches docs by their on-disk path, so we
        // still need a real file for the command to find. Write to a temp path and
        // schedule its deletion, mirroring RhinoMcpHost.StopByPort.
        string writePath = hasPath
            ? path!
            : Path.Combine(Path.GetTempPath(), $"rhmcp-close-{Guid.NewGuid():N}.3dm");

        string? dir = Path.GetDirectoryName(writePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Directory does not exist: {dir}");

        // UpdateDocumentPath=true so `_-Close "{writePath}"` resolves the doc by path.
        FileWriteOptions options = new ()
        {
            SuppressDialogBoxes = true,
            WriteUserData = true,
            UpdateDocumentPath = true,
        };

        if (!doc.WriteFile(writePath, options))
            throw new InvalidOperationException($"Failed to write before closing: {writePath}");

        doc.Modified = false;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"_-Close \"{writePath}\"", false);

        if (hasPath)
            return $"Document saved to {writePath} and closed.";

        // Mac defers the doc close via Cocoa performSelector:afterDelay:0.1. Wait
        // past that, then delete the temp file. Fire-and-forget so we don't block.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            try
            { File.Delete(writePath); }
            catch { /* OS temp sweep will get it */ }
        });

        return "Document closed without saving.";
    }
}
