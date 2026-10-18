using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rhino.AI.UI;

// The panel's listener is loopback with no authentication, so it serves only what was deliberately published here, never a path the caller composed.
internal static class ServedImages
{
    public const string Route = "/image/";

    private static Dictionary<string, string> ByIdentity { get; } = new(StringComparer.Ordinal);
    private static Dictionary<string, string> ByFileName { get; } = new(StringComparer.Ordinal);

    private static Dictionary<string, string> MediaTypes { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
    };

    public static bool IsImage(string path) => MediaTypes.ContainsKey(Path.GetExtension(path));

    public static string MediaType(string path) =>
        MediaTypes.TryGetValue(Path.GetExtension(path), out string? found) ? found : "application/octet-stream";

    public static PanelImage? Publish(string path, string? displayName = null)
    {
        long bytes;
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length == 0)
                return null;
            bytes = file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        string id = Identify(path);
        string name = !string.IsNullOrWhiteSpace(displayName) ? displayName! : Path.GetFileName(path);
        lock (ByIdentity)
        {
            ByIdentity[id] = path;
            ByFileName[id] = name;
        }

        return new PanelImage(id, name, Route + id, bytes);
    }

    public static string? Resolve(string id)
    {
        lock (ByIdentity)
            return ByIdentity.TryGetValue(id, out string? path) ? path : null;
    }

    public static string? ResolveFileName(string id)
    {
        lock (ByIdentity)
            return ByFileName.TryGetValue(id, out string? name) ? name : null;
    }

    public static string? ResolveNamedPath(string id)
    {
        string? path;
        string? name;
        lock (ByIdentity)
        {
            if (!ByIdentity.TryGetValue(id, out path))
                return null;
            ByFileName.TryGetValue(id, out name);
        }

        if (string.IsNullOrWhiteSpace(name) || string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal))
            return path;

        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "RhinoAI", "Images");
            Directory.CreateDirectory(dir);
            string staged = Path.Combine(dir, name!);
            FileInfo src = new(path);
            FileInfo dst = new(staged);
            if (!dst.Exists || dst.Length != src.Length || dst.LastWriteTimeUtc != src.LastWriteTimeUtc)
            {
                File.Copy(path, staged, overwrite: true);
                File.SetLastWriteTimeUtc(staged, src.LastWriteTimeUtc);
            }
            return staged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    // get_viewport_image saves here, so an image under it is a capture of the model rather than one an agent drew.
    public static string CaptureFolder => Path.Combine(Path.GetTempPath(), "RhinoAI", "Captures");

    public static bool IsCapture(string path)
    {
        try
        {
            string folder = Path.GetFullPath(CaptureFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

    // The model a published image is named after. An agent that delegates drawing (imageModel set) has its
    // generated images named after that model; a viewport capture, or an image model the user picked outright,
    // keeps the chosen model.
    public static string? ModelForImage(string path, string? chosenModel, string? imageModel)
    {
        if (string.IsNullOrWhiteSpace(imageModel) || IsCapture(path))
            return chosenModel;
        if (chosenModel is not null && chosenModel.EndsWith("-image", StringComparison.OrdinalIgnoreCase))
            return chosenModel;
        return imageModel;
    }

    public static string FormatRenderName(string path, string? docTitle, string? agentName, string? modelName)
    {
        DateTime timestamp;
        try
        {
            FileInfo file = new(path);
            timestamp = file.Exists ? file.LastWriteTime : DateTime.Now;
        }
        catch
        {
            timestamp = DateTime.Now;
        }

        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";

        string rawDoc = string.IsNullOrWhiteSpace(docTitle)
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(docTitle!.Trim());
        if (string.IsNullOrWhiteSpace(rawDoc))
            rawDoc = "Untitled";

        string docPart = SanitizeSegment(rawDoc, "Untitled");
        string agentPart = SanitizeSegment(agentName, "Agent");
        string modelPart = SanitizeSegment(modelName, "default");
        string timePart = timestamp.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        return $"{docPart}_{agentPart}_{modelPart}_{timePart}{ext.ToLowerInvariant()}";
    }

    private static string SanitizeSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(value!.Length);
        foreach (char c in value.Trim())
        {
            if (Array.IndexOf(invalid, c) < 0)
                sb.Append(c);
        }

        return sb.Length > 0 ? sb.ToString() : fallback;
    }

    private static string Identify(string path)
    {
        using SHA256 sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
        return BitConverter.ToString(digest, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
    }
}
