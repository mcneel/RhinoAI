using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using RhinoAI.WebPanel;

namespace RhinoAI.StreamJson.Tests;

// Writes a real event stream, serialised by the real serialiser, to a file that the panel's own
// browser checks replay (rhino/panel/tools/verify.mjs). That is the only place the two languages
// actually meet, so a shape change on this side fails over there rather than in Rhino.
//
// The file is committed. Regenerating it is the point: a diff here is a protocol change.
[TestFixture]
public sealed class PanelContractTests
{
    private const string ContractPath = "rhino/panel/tests/host-events.json";

    [Test]
    public void Writes_a_representative_stream_for_the_panel_to_replay()
    {
        Conversation convo = new(Guid.Parse("2f8b1c40-5d3e-4a91-9c62-7f0a8e14d5b3"), "claude", "tower-study.3dm");
        List<PanelEvent> sent = [];
        ConversationFeed feed = new(convo, sent.Add);

        sent.Add(new HelloEvent(new PanelHost(
            "Rhinoceros", "9.0.0", "macos", "tower-study.3dm",
            new PanelCapabilities(Attachments: false, ViewportCapture: true, UndoTurn: false, Grasshopper: true))));
        sent.Add(new ThemeEvent("dark"));
        sent.Add(new AgentsEvent(
            [new PanelAgent("claude", "Claude Code", "claude-opus-5", "Opus 5", "ready", null, true),
             new PanelAgent("gemini", "Gemini CLI", "gemini-3-pro", "Gemini 3 Pro", "missing", "'gemini' was not found", true)],
            "claude"));

        convo.BeginTurn("What is on the Facade layer?");
        feed.Replay();

        convo.Record(TurnEventKind.AssistantText, "Checking the ");
        convo.Record(TurnEventKind.AssistantText, "layer now.\n\n");
        feed.Pump();

        convo.Record(TurnEventKind.ToolUse, "list_objects", "{\"layer\":\"Facade\"}", string.Empty, "call-1");
        feed.Pump();

        convo.CompleteToolCall("call-1", "{\"Ok\":true,\"count\":312}");
        feed.Pump();

        convo.Record(TurnEventKind.AssistantText, "**312 objects**, mostly planar breps.\n\n```python\nprint(312)\n```");
        convo.RecordUsage(new TokenUsage(12480, 1120, 0.09m));
        convo.CompleteTurn();
        feed.Pump();

        convo.SetPendingQuestion(new PendingQuestion("Split the worst panels?", ["Yes", "No"], AskUserMode.Single));
        feed.Pump();

        StringBuilder json = new();
        json.AppendLine("[");
        for (int i = 0; i < sent.Count; i++)
            json.AppendLine($"  {PanelJson.Serialize(sent[i])}{(i < sent.Count - 1 ? "," : string.Empty)}");
        json.Append(']');

        // Timestamps come from the clock, and a file that changes on every run makes a real
        // protocol change invisible in the diff. Pin them.
        string stable = Regex.Replace(
            json.ToString(),
            @"\d{4}-\d{2}-\d{2}T[\d:.]+\\u002B00:00",
            "2026-01-01T09:00:00.0000000\u002B00:00");

        string path = Path.Combine(RepoRoot(), ContractPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, stable);

        Assert.That(sent.OfType<TurnTextEvent>().Count(), Is.GreaterThanOrEqualTo(3));
        Assert.That(sent.OfType<TurnToolPatchEvent>(), Is.Not.Empty);
        Assert.That(sent.OfType<QuestionEvent>(), Is.Not.Empty);
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
