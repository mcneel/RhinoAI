using System.Text;
using Acp;
using RhMcp;
using ContentBlock = Acp.ContentBlock;

namespace RhMcp.StreamJson.Tests;

// AcpMessageMapper is the UserMessage/attachment <-> ACP ContentBlock seam. The agent has no
// filesystem access, so every attachment must arrive inline: images base64, text files fenced.
// These pin that mapping plus the empty/edge shapes and the TextOf reverse direction.
[TestFixture]
public sealed class AcpMessageMapperTests
{
    private static Attachment Image(byte[] data, string media = "image/png") =>
        new(AttachmentKind.Image, "shot.png", media, data);

    private static Attachment TextFile(string body, string name = "notes.txt") =>
        new(AttachmentKind.TextFile, name, "text/plain", Encoding.UTF8.GetBytes(body));

    [Test]
    public void Text_only_message_maps_to_a_single_text_block()
    {
        ContentBlock[] blocks = AcpMessageMapper.Prompt(UserMessage.FromText("draw a box"));

        Assert.That(blocks, Has.Length.EqualTo(1));
        Assert.That(((TextContentBlock)blocks[0]).Text, Is.EqualTo("draw a box"));
    }

    [Test]
    public void Empty_text_with_no_attachments_yields_no_blocks()
    {
        ContentBlock[] blocks = AcpMessageMapper.Prompt(UserMessage.FromText(string.Empty));

        Assert.That(blocks, Is.Empty);
    }

    [Test]
    public void Image_attachment_is_inlined_as_base64_with_its_media_type()
    {
        byte[] payload = [1, 2, 3, 4];
        UserMessage message = new(string.Empty, [Image(payload, "image/jpeg")]);

        ContentBlock[] blocks = AcpMessageMapper.Prompt(message);

        Assert.That(blocks, Has.Length.EqualTo(1));
        ImageContentBlock image = (ImageContentBlock)blocks[0];
        Assert.That(image.Data, Is.EqualTo(Convert.ToBase64String(payload)));
        Assert.That(image.MimeType, Is.EqualTo("image/jpeg"));
    }

    [Test]
    public void Text_file_attachment_is_fenced_with_its_name_and_decoded_body()
    {
        UserMessage message = new(string.Empty, [TextFile("hello world", "spec.md")]);

        ContentBlock[] blocks = AcpMessageMapper.Prompt(message);

        Assert.That(blocks, Has.Length.EqualTo(1));
        Assert.That(((TextContentBlock)blocks[0]).Text, Is.EqualTo("```spec.md\nhello world\n```"));
    }

    [Test]
    public void Text_then_attachments_preserve_order_text_first()
    {
        UserMessage message = new("look at this", [Image([9]), TextFile("body")]);

        ContentBlock[] blocks = AcpMessageMapper.Prompt(message);

        Assert.That(blocks, Has.Length.EqualTo(3));
        Assert.That(blocks[0], Is.InstanceOf<TextContentBlock>());
        Assert.That(((TextContentBlock)blocks[0]).Text, Is.EqualTo("look at this"));
        Assert.That(blocks[1], Is.InstanceOf<ImageContentBlock>());
        Assert.That(blocks[2], Is.InstanceOf<TextContentBlock>());
    }

    [Test]
    public void TextOf_returns_the_text_of_a_text_block_and_empty_for_non_text()
    {
        Assert.That(AcpMessageMapper.TextOf(new TextContentBlock { Text = "hi" }), Is.EqualTo("hi"));
        Assert.That(AcpMessageMapper.TextOf(new ImageContentBlock { Data = "x", MimeType = "image/png" }), Is.Empty);
    }
}
