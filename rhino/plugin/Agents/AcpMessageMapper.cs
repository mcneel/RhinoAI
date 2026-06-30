using System;
using System.Collections.Generic;
using System.Text;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from the global RhMcp.Server.ContentBlock

namespace RhMcp;

// Maps between the plugin's UserMessage/attachments and ACP content blocks. The agent has no
// filesystem access, so every attachment is delivered inline: images as base64, text files fenced.
internal static class AcpMessageMapper
{
    public static ContentBlock[] Prompt(UserMessage message)
    {
        List<ContentBlock> blocks = new();
        if (message.Text.Length > 0)
            blocks.Add(new TextContentBlock { Text = message.Text });

        foreach (Attachment attachment in message.Attachments)
        {
            blocks.Add(attachment.Kind switch
            {
                AttachmentKind.Image => new ImageContentBlock
                {
                    Data = Convert.ToBase64String(attachment.Data),
                    MimeType = attachment.MediaType,
                },
                AttachmentKind.TextFile => new TextContentBlock
                {
                    Text = $"```{attachment.Name}\n{Encoding.UTF8.GetString(attachment.Data)}\n```",
                },
                _ => new TextContentBlock { Text = string.Empty },
            });
        }

        return blocks.ToArray();
    }

    // The displayable text of a content block (empty for non-text kinds).
    public static string TextOf(ContentBlock block) =>
        block is TextContentBlock text ? text.Text : string.Empty;
}
