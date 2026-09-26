namespace Rhino.AI;

/// <summary>Tagged with its issuer because a forked transcript can reach a different vendor, where the blob is meaningless and usually a 400.</summary>
public sealed record ThoughtSignature(string Vendor, string? Id, string Value, bool IsRedacted = false);
