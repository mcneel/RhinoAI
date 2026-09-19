using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rhino.AI.UI;

// The panel's listener is loopback with no authentication, so it serves only what was deliberately published here, never a path the caller composed.
internal static class ServedImages
{
    public const string Route = "/image/";

    private static Dictionary<string, string> ByIdentity { get; } = new(StringComparer.Ordinal);

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

    public static PanelImage? Publish(string path)
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
        lock (ByIdentity)
            ByIdentity[id] = path;

        return new PanelImage(id, Path.GetFileName(path), Route + id, bytes);
    }

    public static string? Resolve(string id)
    {
        lock (ByIdentity)
            return ByIdentity.TryGetValue(id, out string? path) ? path : null;
    }

    private static string Identify(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)), 0, 12).ToLowerInvariant();
}
