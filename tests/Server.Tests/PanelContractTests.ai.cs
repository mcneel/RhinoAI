using System.Text;
using System.Text.RegularExpressions;

using NUnit.Framework;
using Rhino.AI.UI;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public sealed class PanelContractTests
{
    private const string ContractReplayedByVerifyMjs = "rhino/panel/tests/host-events.json";

    private const string ClaudeId = "d07df3c4-2c1d-40a1-a90c-7e85f1acae62";
    private const string CodexId = "82e63c9f-eb84-410c-9a3e-f47c6528f69f";
    private const string DocTitle = "tower-study.3dm";
    private const string CodexStyleRenderName = "exec-91909287-454a-419b-86b5-6f883204d8cd.png";

    private const string PinnedTime = "2026-01-01T09:00:00.0000000+00:00";
    private const string PinnedImageId = "a3f1c09e42b7d5e8f6019c3b";

    private static byte[] Png { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private string RenderFolder { get; set; } = string.Empty;

    [SetUp]
    public void CreateRenderFolder()
    {
        RenderFolder = Path.Combine(Path.GetTempPath(), $"rai-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RenderFolder);
    }

    [TearDown]
    public void RemoveRenderFolder() => Directory.Delete(RenderFolder, recursive: true);

    [Test]
    public void Writes_the_host_events_the_panel_replays()
    {
        Conversation convo = new(Guid.Parse("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3"), ClaudeId, DocTitle);
        List<PanelEvent> sent = [];
        ConversationFeed feed = new(convo, sent.Add, _ => [RenderFolder]);

        sent.Add(new HelloEvent(
            new PanelHost("Rhinoceros", "9.0.0", "macos", DocTitle,
                new PanelCapabilities(Attachments: true, ViewportCapture: true, UndoTurn: false, Grasshopper: true, PluginCommands: true)),
            PanelStrings.LanguageTag(),
            PanelStrings.Localized()));
        sent.Add(new ZoomEvent("set", 1.0));
        sent.Add(Theme());
        sent.Add(new AgentsEvent(
            [new PanelAgent(ClaudeId, "Claude", "opus", "Opus (latest)", "ready", null),
             new PanelAgent(CodexId, "Codex", "default", "Default", "missing", null)],
            ClaudeId));

        Turn turn = convo.BeginTurn("What is on the Facade layer?");

        // GeneratedImages caches its folder listing from the first pump, so a render stamped any later would never be seen.
        string render = Path.Combine(RenderFolder, CodexStyleRenderName);
        File.WriteAllBytes(render, Png);
        File.SetLastWriteTimeUtc(render, turn.StartedAt.UtcDateTime);

        feed.Replay();

        convo.Record(TurnEventKind.AssistantText, "Checking the ");
        convo.Record(TurnEventKind.AssistantText, "layer now.\n\n");
        feed.Pump();

        convo.Record(TurnEventKind.ToolUse, "mcp__rhino__list_objects", "{\"layer\":\"Facade\"}", string.Empty, "call-1");
        feed.Pump();

        convo.CompleteToolCall("call-1", "{\"Ok\":true,\"count\":312}");
        feed.Pump();

        string unrecognisedAndLeftRunning = "mcp__rhino__probe_intersection";
        convo.Record(TurnEventKind.ToolUse, unrecognisedAndLeftRunning, "{}", string.Empty, "call-2");
        feed.Pump();

        convo.Record(TurnEventKind.ToolUse, "mcp__rhino__run_python", "{\"code\":\"boom()\"}", string.Empty, "call-3");
        feed.Pump();

        convo.CompleteToolCall("call-3", "\"NameError: name 'boom' is not defined\"", failed: true);
        feed.Pump();

        string cancellableAndLeftRunning = "mcp__rhino__run_command";
        convo.Record(TurnEventKind.ToolUse, cancellableAndLeftRunning, "{\"command\":\"_Box 0,0,0 10,10,10\"}", string.Empty, "call-4");
        feed.Pump();

        convo.Record(TurnEventKind.AssistantText, "**312 objects**, mostly planar breps.\n\n```python\nprint(312)\n```");
        convo.RecordUsage(new TokenUsage(12480, 1120));
        convo.CompleteTurn();
        feed.Pump();

        PendingQuestion[] onePromptPosingTwoQuestions =
        [
            new("Split the worst panels?", ["Yes", "No"], AskUserMode.Single),
            new("Which levels should I check?", ["L12", "L13", "L14"], AskUserMode.Multi),
        ];
        convo.AddPendingQuestions(onePromptPosingTwoQuestions);
        feed.Pump();

        Assert.That(sent.OfType<TurnTextEvent>().Count(), Is.GreaterThanOrEqualTo(3));
        Assert.That(sent.OfType<TurnToolPatchEvent>(), Is.Not.Empty);
        Assert.That(sent.OfType<TurnImageEvent>().Count(), Is.EqualTo(1));
        Assert.That(sent.OfType<QuestionEvent>().Count(), Is.EqualTo(2));

        StringBuilder json = new();
        json.AppendLine("[");
        for (int i = 0; i < sent.Count; i++)
            json.AppendLine($"  {PanelJson.Serialize(sent[i])}{(i < sent.Count - 1 ? "," : string.Empty)}");
        json.Append(']');

        string imageIdHashedFromTempPath = sent.OfType<TurnImageEvent>().Single().Image.Id;
        string stableAcrossRunsAndMachines = Regex
            .Replace(json.ToString(), @"\d{4}-\d{2}-\d{2}T[\d:.]+(\\u002B|-)\d{2}:\d{2}", PinnedTime)
            .Replace(imageIdHashedFromTempPath, PinnedImageId);

        File.WriteAllText(Path.Combine(RepoRoot(), ContractReplayedByVerifyMjs), stableAcrossRunsAndMachines);
    }

    private static ThemeEvent Theme()
    {
        PanelTheme.Palette palette = new(
            Chrome: new PanelTheme.Rgb(0.14f, 0.15f, 0.16f),
            Field: new PanelTheme.Rgb(0.10f, 0.11f, 0.12f),
            Text: new PanelTheme.Rgb(0.91f, 0.92f, 0.93f),
            Dim: new PanelTheme.Rgb(0.64f, 0.65f, 0.68f),
            Border: new PanelTheme.Rgb(0.30f, 0.30f, 0.32f),
            Accent: new PanelTheme.Rgb(0.28f, 0.55f, 0.90f),
            AccentText: new PanelTheme.Rgb(1f, 1f, 1f),
            Link: new PanelTheme.Rgb(0.44f, 0.66f, 1f),
            Selection: new PanelTheme.Rgb(0.18f, 0.29f, 0.46f),
            SelectionText: new PanelTheme.Rgb(1f, 1f, 1f));

        Dictionary<string, string> tokens = PanelTheme.Tokens(palette);
        foreach (KeyValuePair<string, string> entry in PanelTheme.Fonts("Helvetica Neue", 13f, windows: false))
            tokens[entry.Key] = entry.Value;

        return new ThemeEvent(PanelTheme.IsDarkTheme(palette) ? "dark" : "light", tokens);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rhino", "panel")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test assembly");
        return dir!.FullName;
    }
}
