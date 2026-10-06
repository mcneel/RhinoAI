using System;
using System.IO;
using System.Collections.Generic;

namespace Rhino.AI;

internal sealed class ImagetoFile : IDisposable
{

    public IReadOnlyList<string> Paths { get; }

    private ImagetoFile(IReadOnlyList<string> paths)
    {
        Paths = paths;
    }

    public static ImagetoFile Write(IEnumerable<ImageContent> images)
    {
        List<string> paths = [];
        foreach (ImageContent image in images)
        {
            string path = Path.Combine(Path.GetTempPath(), $"rhinoai-{Guid.NewGuid():N}{ExtensionFor(image.MediaType)}");
            File.WriteAllBytes(path, image.Bytes);
            paths.Add(path);
        }

        return new ImagetoFile(paths);
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".png",
    };

    public void Dispose()
    {
        foreach (string path in Paths)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

}
