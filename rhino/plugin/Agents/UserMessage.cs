using System.Collections.Generic;

namespace RhMcp;

// The single message type threaded through IAgentRunner.PromptAsync.
internal sealed record UserMessage(string Text, IReadOnlyList<Attachment> Attachments)
{
    // Named FromText (not Text) because the positional record already owns a `Text` property.
    public static UserMessage FromText(string text) => new(text, []);
}
