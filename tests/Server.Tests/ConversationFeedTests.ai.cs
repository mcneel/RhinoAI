using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ConversationFeedTests
{
    private string Folder { get; set; } = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        Folder = Path.Combine(Path.GetTempPath(), $"rai-feed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(Folder, "generated"));
    }

    [TearDown]
    public void RemoveFolder() => Directory.Delete(Folder, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(Folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private List<TurnImageEvent> PumpImages(string toolResult)
    {
        Conversation conversation = new(Guid.NewGuid(), "Gemini", "Doc");
        conversation.BeginTurn("render it");
        conversation.Record(TurnEventKind.ToolUse, "get_viewport_image", "{}", toolResult, "call-1");

        List<PanelEvent> events = [];
        ConversationFeed feed = new(conversation, events.Add, Path.Combine(Folder, "generated"));
        feed.Pump();
        return events.OfType<TurnImageEvent>().ToList();
    }

    [Test]
    public void The_same_capture_offloaded_to_a_second_path_is_shown_once()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
        string capture = Write("Captures/viewport_1.jpg", jpeg).Replace('\\', '/');
        string offloaded = Write("steps/2/media_0.jpg", jpeg).Replace('\\', '/');

        List<TurnImageEvent> images = PumpImages(
            $$$"""{"viewport":{"path":"{{{capture}}}"}}""" + $"\n[Resource offloaded to file:///{offloaded}]");

        Assert.That(images, Has.Count.EqualTo(1));
    }

    [Test]
    public void Different_images_in_one_result_are_both_shown()
    {
        string first = Write("a.jpg", [0xFF, 0xD8, 1]).Replace('\\', '/');
        string second = Write("b.jpg", [0xFF, 0xD8, 2]).Replace('\\', '/');

        List<TurnImageEvent> images = PumpImages($"\"{first}\" and \"{second}\"");

        Assert.That(images, Has.Count.EqualTo(2));
    }
}
