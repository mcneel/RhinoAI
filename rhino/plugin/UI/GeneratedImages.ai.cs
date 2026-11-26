using System.IO;

namespace Rhino.AI.UI;

// Codex's image generator saves a render to disk and the model is free never to say so, which is the whole of RH-98659.
internal sealed class GeneratedImages(Func<Guid, IReadOnlyList<string>> foldersFor)
{
    private static TimeSpan ListingLife { get; } = TimeSpan.FromMilliseconds(500);

    private Func<Guid, IReadOnlyList<string>> FoldersFor { get; } = foldersFor;
    private Guid ListedFor { get; set; }
    private DateTimeOffset ListedAt { get; set; } = DateTimeOffset.MinValue;
    private IReadOnlyList<Produced> Listed { get; set; } = [];

    private readonly record struct Produced(string Path, DateTimeOffset WrittenAt);

    public IReadOnlyList<string> Between(Guid sessionId, DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (sessionId != ListedFor || now - ListedAt >= ListingLife)
        {
            Listed = List(FoldersFor(sessionId));
            ListedFor = sessionId;
            ListedAt = now;
        }

        List<string> within = [];
        foreach (Produced file in Listed)
            if (file.WrittenAt >= from && file.WrittenAt <= to)
                within.Add(file.Path);
        return within;
    }

    private static List<Produced> List(IReadOnlyList<string> folders)
    {
        List<Produced> files = [];
        foreach (string folder in folders)
        {
            try
            {
                DirectoryInfo found = new(folder);
                if (!found.Exists)
                    continue;

                foreach (FileInfo file in found.EnumerateFiles())
                {
                    bool isStillBeingWritten = file.Length == 0;
                    if (!isStillBeingWritten && ServedImages.IsImage(file.Name))
                        files.Add(new Produced(file.FullName, file.LastWriteTimeUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }
        return files;
    }
}
