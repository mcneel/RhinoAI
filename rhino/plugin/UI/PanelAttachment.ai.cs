namespace Rhino.AI.UI;

// Bytes round-trip as a data URL because the composer owns its own state and the agent has no filesystem.
internal sealed record PanelAttachment(
    string Id,
    string Kind,
    string Name,
    string MediaType,
    long Bytes,
    string? DataUrl)
{
    public static PanelAttachment From(string id, AttachmentKind kind, string name, string mediaType, byte[] data) =>
        new(id, Spelling(kind), name, mediaType, data.Length, $"data:{mediaType};base64,{Convert.ToBase64String(data)}");

    // No DataUrl: a sent turn draws a chip, not a thumbnail, and the bytes already went to the agent.
    public static PanelAttachment Describe(string id, AttachmentInfo info) =>
        new(id, Spelling(info.Kind), info.Name, info.MediaType, info.Bytes, null);

    public Attachment? ToAttachment()
    {
        if (DataUrl is not { Length: > 0 } url)
            return null;

        int comma = url.IndexOf(',');
        if (comma < 0 || !url.Substring(0, comma).EndsWith(";base64", StringComparison.Ordinal))
            return null;

        byte[] data;
        try
        {
            data = Convert.FromBase64String(url.Substring(comma + 1));
        }
        catch (FormatException)
        {
            return null;
        }

        return Kind switch
        {
            "image" => new Attachment(AttachmentKind.Image, Name, MediaType, data),
            "text" when LooksLikeText(data) => new Attachment(AttachmentKind.TextFile, Name, MediaType, data),
            _ => null,
        };
    }

    public static bool LooksLikeText(byte[] data) => Array.IndexOf(data, (byte)0) < 0;

    private static string Spelling(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => "image",
        AttachmentKind.TextFile => "text",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
