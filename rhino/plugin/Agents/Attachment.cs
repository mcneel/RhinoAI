namespace RhMcp;

internal enum AttachmentKind
{
    Image,
    TextFile,
}

// Dumb data object; Data is the raw payload (Part 4 consumes it).
internal sealed record Attachment(AttachmentKind Kind, string Name, string MediaType, byte[] Data);
