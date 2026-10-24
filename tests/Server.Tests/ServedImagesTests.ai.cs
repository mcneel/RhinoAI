using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ServedImagesTests
{
    private const string Chat = "gemini-3.8-flash-high";
    private const string Image = "gemini-3.1-flash-image";

    private static string Generated => Path.Combine(Path.GetTempPath(), "agy-brain", "render_1.jpg");
    private static string Capture => Path.Combine(ServedImages.CaptureFolder, "viewport_1.jpg");

    [Test]
    public void A_delegated_image_is_named_after_the_image_model() =>
        Assert.That(ServedImages.ModelForImage(Generated, Chat, Image), Is.EqualTo(Image));

    [Test]
    public void A_viewport_capture_keeps_the_chat_model() =>
        Assert.That(ServedImages.ModelForImage(Capture.Replace('\\', '/'), Chat, Image), Is.EqualTo(Chat));

    [Test]
    public void An_agent_without_an_image_model_keeps_its_own() =>
        Assert.That(ServedImages.ModelForImage(Generated, "gpt-5.5", ""), Is.EqualTo("gpt-5.5"));

    [Test]
    public void An_image_model_the_user_chose_outright_wins() =>
        Assert.That(ServedImages.ModelForImage(Generated, "gemini-3-pro-image", Image), Is.EqualTo("gemini-3-pro-image"));
}
