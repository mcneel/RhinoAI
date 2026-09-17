using NUnit.Framework;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ToolModeTests
{
    // ToolMode is internal, so the cases speak its text form and the fixture stays public for NUnit.
    [TestCase(true, false, "on")]
    [TestCase(true, true, "ask")]
    [TestCase(false, false, "off")]
    [TestCase(false, true, "off")]
    public void DefaultFor_ReadsTheDeclaredFlags(bool enabled, bool confirm, string expected)
    {
        Assert.That(ToolModes.Format(ToolModes.DefaultFor(enabled, confirm)), Is.EqualTo(expected));
    }

    [TestCase("off")]
    [TestCase("on")]
    [TestCase("ask")]
    public void Format_AndParse_RoundTrip(string text)
    {
        Assert.That(ToolModes.TryParse(text, out ToolMode parsed), Is.True);
        Assert.That(ToolModes.Format(parsed), Is.EqualTo(text));
    }

    [Test]
    public void Parse_IgnoresCaseAndWhitespace()
    {
        Assert.That(ToolModes.TryParse("  ASK ", out ToolMode mode), Is.True);
        Assert.That(mode, Is.EqualTo(ToolMode.Ask));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("maybe")]
    public void Parse_RejectsUnknownText(string? text)
    {
        Assert.That(ToolModes.TryParse(text, out _), Is.False);
    }

    [Test]
    public void Entry_RoundTrips()
    {
        string entry = ToolModes.FormatEntry("script_editor_run", ToolMode.Ask);

        Assert.That(entry, Is.EqualTo("script_editor_run=ask"));
        Assert.That(ToolModes.TryParseEntry(entry, out string name, out ToolMode mode), Is.True);
        Assert.That(name, Is.EqualTo("script_editor_run"));
        Assert.That(mode, Is.EqualTo(ToolMode.Ask));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("=on")]
    [TestCase("script_editor_run")]
    [TestCase("script_editor_run=maybe")]
    public void Entry_RejectsMalformedText(string? entry)
    {
        Assert.That(ToolModes.TryParseEntry(entry, out _, out _), Is.False);
    }

    [Test]
    public void IsFor_MatchesTheNameCaseInsensitively()
    {
        Assert.That(ToolModes.IsFor("Script_Editor_Run=off", "script_editor_run"), Is.True);
        Assert.That(ToolModes.IsFor("script_editor_read=off", "script_editor_run"), Is.False);
        Assert.That(ToolModes.IsFor("garbage", "script_editor_run"), Is.False);
    }
}
