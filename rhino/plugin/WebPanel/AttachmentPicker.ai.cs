using System.IO;

using Eto.Forms;

namespace Rhino.AI.WebPanel;

// Picking lives here, not in the page: a WKWebView has no open panel, so <input type="file"> does nothing on macOS.
internal static class AttachmentPicker
{
    private const long MaxBytes = 10 * 1024 * 1024;

    private static Dictionary<string, string> ImageTypes { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    private static string[] TextExtensions { get; } =
        [".txt", ".md", ".json", ".csv", ".xml", ".yml", ".yaml", ".log", ".py", ".cs", ".js", ".ts", ".html", ".css", ".sql"];

    public static IReadOnlyList<PanelAttachment> Pick(Control parent, Action<string> warn)
    {
        OpenFileDialog dialog = new() { Title = Rhino.UI.LOC.STR("Attach files"), MultiSelect = true };
        dialog.Filters.Add(new FileFilter(Rhino.UI.LOC.STR("Images and text"), [.. ImageTypes.Keys, .. TextExtensions]));
        dialog.Filters.Add(new FileFilter(Rhino.UI.LOC.STR("Images"), [.. ImageTypes.Keys]));
        dialog.Filters.Add(new FileFilter(Rhino.UI.LOC.STR("All files"), ".*"));

        if (dialog.ShowDialog(parent) != DialogResult.Ok)
            return [];

        List<PanelAttachment> picked = [];
        foreach (string path in dialog.Filenames)
            if (Read(path, warn) is { } attachment)
                picked.Add(attachment);
        return picked;
    }

    private static PanelAttachment? Read(string path, Action<string> warn)
    {
        string name = Path.GetFileName(path);
        try
        {
            long length = new FileInfo(path).Length;
            if (length > MaxBytes)
            {
                warn(string.Format(
                    Rhino.UI.LOC.STR("{0} is {1} MB, over the {2} MB attachment limit."),
                    name,
                    length / (1024 * 1024),
                    MaxBytes / (1024 * 1024)));
                return null;
            }

            byte[] data = File.ReadAllBytes(path);
            if (ImageTypes.TryGetValue(Path.GetExtension(path), out string? mediaType))
                return PanelAttachment.From(NextId(), AttachmentKind.Image, name, mediaType, data);

            if (!PanelAttachment.LooksLikeText(data))
            {
                warn(string.Format(Rhino.UI.LOC.STR("{0} is neither an image nor a text file, so it cannot be attached."), name));
                return null;
            }
            return PanelAttachment.From(NextId(), AttachmentKind.TextFile, name, "text/plain", data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            warn(string.Format(Rhino.UI.LOC.STR("Could not read {0}: {1}"), name, ex.Message));
            return null;
        }
    }

    private static string NextId() => $"host-{Guid.NewGuid():N}";
}
