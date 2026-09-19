using System.IO;

namespace Rhino.AI.UI;

// Codex's image generator saves a render under the CODEX_HOME the plug-in hands it and the model is free never to say so, which is the whole of RH-98659.
internal sealed class GeneratedImages
{
    private static TimeSpan ListingLife { get; } = TimeSpan.FromMilliseconds(500);

    private string? Root { get; }
    private DateTimeOffset ListedAt { get; set; } = DateTimeOffset.MinValue;
    private Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);

    private readonly record struct Produced(string Path, DateTimeOffset WrittenAt);

    private sealed record Folder(DateTimeOffset TouchedAt, bool IsSettled, IReadOnlyList<Produced> Files);

    public GeneratedImages(string? root) => Root = root;

    public IReadOnlyList<string> Between(DateTimeOffset from, DateTimeOffset to)
    {
        Rescan();

        List<string> within = [];
        foreach (Folder folder in Folders.Values)
            foreach (Produced file in folder.Files)
                if (file.WrittenAt >= from && file.WrittenAt <= to)
                    within.Add(file.Path);
        return within;
    }

    private void Rescan()
    {
        if (Root is not { Length: > 0 } root)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - ListedAt < ListingLife)
            return;
        ListedAt = now;

        try
        {
            DirectoryInfo top = new(root);
            if (!top.Exists)
                return;

            HashSet<string> live = new(StringComparer.Ordinal);
            Reread(top, live);
            foreach (DirectoryInfo child in top.EnumerateDirectories("*", SearchOption.AllDirectories))
                Reread(child, live);

            foreach (string gone in Folders.Keys.Where(known => !live.Contains(known)).ToArray())
                Folders.Remove(gone);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Folders.Clear();
        }
    }

    private void Reread(DirectoryInfo folder, HashSet<string> live)
    {
        live.Add(folder.FullName);

        DateTimeOffset touchedAt = folder.LastWriteTimeUtc;
        if (Folders.TryGetValue(folder.FullName, out Folder? known) && known.IsSettled && known.TouchedAt == touchedAt)
            return;

        List<Produced> files = [];
        bool isSettled = true;
        foreach (FileInfo file in folder.EnumerateFiles())
        {
            if (!ServedImages.IsImage(file.Name))
                continue;
            if (file.Length == 0)
            {
                isSettled = false;
                continue;
            }
            files.Add(new Produced(file.FullName, file.LastWriteTimeUtc));
        }

        Folders[folder.FullName] = new Folder(touchedAt, isSettled, files);
    }
}
