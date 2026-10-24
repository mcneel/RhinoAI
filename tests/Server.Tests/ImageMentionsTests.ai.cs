using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ImageMentionsTests
{
    private string Folder { get; set; } = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        Folder = Path.Combine(Path.GetTempPath(), $"rai-mentions-{Guid.NewGuid():N}", "render out");
        Directory.CreateDirectory(Folder);
    }

    [TearDown]
    public void RemoveFolder() => Directory.Delete(Path.GetDirectoryName(Folder)!, recursive: true);

    private string Write(string name)
    {
        string path = Path.Combine(Folder, name);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        return path;
    }

    [Test]
    public void A_bracketed_destination_carries_a_path_that_has_a_space_in_it()
    {
        string path = Write("exec-1.png");

        Assert.That(ImageMentions.In($"The image is saved here:\n\n[Open the rendered image](<{path}>)"),
            Is.EqualTo(new[] { path }));
    }

    [Test]
    public void A_percent_encoded_destination_is_unescaped_before_it_is_looked_for()
    {
        string path = Write("plain.png");

        Assert.That(ImageMentions.In($"[render]({path.Replace(" ", "%20")})"), Is.EqualTo(new[] { path }));
    }

    [Test]
    public void A_quoted_path_in_a_tool_result_is_found()
    {
        string path = Write("saved.jpg");

        Assert.That(ImageMentions.In($$"""{"Ok":true,"output":"{{path}}"}"""), Is.EqualTo(new[] { path }));
    }

    [Test]
    public void A_bare_path_with_no_spaces_is_found_in_prose()
    {
        string path = Path.Combine(Path.GetDirectoryName(Folder)!, "bare.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

        Assert.That(ImageMentions.In($"Wrote it to {path} just now."), Is.EqualTo(new[] { path }));
    }

    [Test]
    public void The_same_file_named_twice_is_reported_once()
    {
        string path = Write("once.png");

        Assert.That(ImageMentions.In($"[a](<{path}>) and again \"{path}\""), Has.Count.EqualTo(1));
    }

    [Test]
    public void A_path_that_does_not_exist_is_not_a_mention()
    {
        string missing = Path.Combine(Folder, "never-written.png");

        Assert.That(ImageMentions.In($"[gone](<{missing}>)"), Is.Empty);
    }

    [Test]
    public void A_file_that_is_not_an_image_is_not_a_mention()
    {
        string path = Write("notes.txt");

        Assert.That(ImageMentions.In($"[notes](<{path}>)"), Is.Empty);
    }

    [Test]
    public void A_tool_result_carrying_a_megabyte_of_base64_is_read_quickly_and_names_nothing()
    {
        string blob = Convert.ToBase64String(new byte[768 * 1024]).Replace("A", "/");
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        IReadOnlyList<string> found = ImageMentions.In($$"""{"content":[{"type":"image","data":"{{blob}}"}]}""");

        Assert.That(found, Is.Empty);
        Assert.That(clock.ElapsedMilliseconds, Is.LessThan(2000));
    }

    [Test]
    public void A_relative_path_has_no_base_to_resolve_against_so_it_is_ignored()
    {
        Write("relative.png");

        Assert.That(ImageMentions.In("[here](relative.png)"), Is.Empty);
    }

    [Test]
    public void A_published_image_is_named_with_doc_agent_model_and_timestamp()
    {
        string path = Write("exec-c5aeecb3-2957-402f-ba54-9a4c68cf6e36.png");
        DateTime stamp = new FileInfo(path).LastWriteTime;

        string name = ServedImages.FormatRenderName(path, "Contemporary house.3dm", "Codex", "gpt-6-astra");
        PanelImage? published = ServedImages.Publish(path, name);
        string? namedPath = ServedImages.ResolveNamedPath(published!.Id);

        Assert.That(name, Is.EqualTo($"Contemporary house_Codex_gpt-6-astra_{stamp:yyyyMMdd_HHmmss}.png"));
        Assert.That(published.Name, Is.EqualTo(name));
        Assert.That(ServedImages.ResolveFileName(published.Id), Is.EqualTo(name));
        Assert.That(Path.GetFileName(namedPath), Is.EqualTo(name));
        Assert.That(File.Exists(namedPath), Is.True);
    }

    [Test]
    public void A_forward_slash_path_inside_escaped_json_tool_result_is_found()
    {
        string path = Write("viewport_capture.jpg");
        string forwardSlash = path.Replace('\\', '/');

        string escapedJson = $"{{\"content\":[{{\"type\":\"text\",\"text\":\"{{\\\"metadata\\\":{{\\\"viewport\\\":{{\\\"path\\\":\\\"{forwardSlash}\\\"}}}}}}\"}}]}}";

        Assert.That(ImageMentions.In(escapedJson), Is.EqualTo(new[] { path }));
    }
}
