using System.Text;

using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class PanelAttachmentTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

    [Test]
    public void The_paper_clip_command_the_panel_sends_is_understood()
    {
        Assert.That(PanelJson.Deserialize("""{"type":"attachments.pick"}"""), Is.TypeOf<PickAttachmentsCommand>());
    }

    [Test]
    public void A_prompt_carries_its_attachments_through_to_the_agent()
    {
        PanelCommand? command = PanelJson.Deserialize($$$"""
            {"type":"prompt","request":{"text":"what is this","attachments":[
              {"id":"host-1","kind":"image","name":"plan.png","mediaType":"image/png","bytes":68,"dataUrl":"{{{Png}}}"}
            ],"context":[]}}
            """);

        Assert.That(command, Is.TypeOf<PromptCommand>());
        PromptRequest request = ((PromptCommand)command!).Request;
        Assert.That(request.Text, Is.EqualTo("what is this"));
        Assert.That(request.Attachments, Has.Count.EqualTo(1));

        Attachment? attachment = request.Attachments[0].ToAttachment();
        Assert.That(attachment, Is.Not.Null);
        Assert.That(attachment!.Kind, Is.EqualTo(AttachmentKind.Image));
        Assert.That(attachment.Name, Is.EqualTo("plan.png"));
        Assert.That(attachment.MediaType, Is.EqualTo("image/png"));
        Assert.That(attachment.Data[..4], Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Test]
    public void A_text_file_arrives_as_its_own_bytes()
    {
        PanelAttachment sent = PanelAttachment.From(
            "host-2", AttachmentKind.TextFile, "notes.md", "text/plain", Encoding.UTF8.GetBytes("# hello"));

        Assert.That(sent.Kind, Is.EqualTo("text"));
        Assert.That(sent.Bytes, Is.EqualTo(7));

        Attachment? attachment = sent.ToAttachment();
        Assert.That(attachment, Is.Not.Null);
        Assert.That(attachment!.Kind, Is.EqualTo(AttachmentKind.TextFile));
        Assert.That(Encoding.UTF8.GetString(attachment.Data), Is.EqualTo("# hello"));
    }

    [Test]
    public void An_attachment_with_no_usable_payload_is_refused_rather_than_sent_empty()
    {
        Assert.That(new PanelAttachment("a", "image", "x.png", "image/png", 4, null).ToAttachment(), Is.Null);
        Assert.That(new PanelAttachment("b", "image", "x.png", "image/png", 4, "data:image/png,raw").ToAttachment(), Is.Null);
        Assert.That(new PanelAttachment("c", "image", "x.png", "image/png", 4, "data:image/png;base64,%%%%").ToAttachment(), Is.Null);
        Assert.That(new PanelAttachment("d", "audio", "x.wav", "audio/wav", 4, Png).ToAttachment(), Is.Null);
        Assert.That(new PanelAttachment("e", "text", "x.3dm", "text/plain", 1, "data:text/plain;base64,AA==").ToAttachment(), Is.Null);
    }

    [Test]
    public void The_host_announces_a_picked_file_in_the_shape_the_panel_reads()
    {
        string json = PanelJson.Serialize(new AttachmentsAddEvent(
            [PanelAttachment.From("host-3", AttachmentKind.Image, "plan.png", "image/png", [0x89, 0x50])]));

        Assert.That(json, Does.Contain("""
            "type":"attachments.add"
            """));
        Assert.That(json, Does.Contain("""
            "kind":"image"
            """));
        Assert.That(json, Does.Contain("""
            "dataUrl":"data:image/png;base64,iVA="
            """));
    }
}
