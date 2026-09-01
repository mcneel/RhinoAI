using NUnit.Framework;
using RhinoAI.Router;

namespace RhinoAI.Router.Tests;

// The per-version path override (debug-build support). Uses a real temp file as
// the override target so File.Exists passes regardless of what Rhino is installed.
[TestFixture]
public sealed class RhinoLocatorOverrideTests
{
    private string _fakeExe = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeExe = Path.Combine(Path.GetTempPath(), "rhmcp-fake-rhino-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(_fakeExe, "");
    }

    [TearDown]
    public void TearDown()
    {
        try { File.Delete(_fakeExe); } catch { /* best effort */ }
    }

    private Dictionary<string, string> Override(string version) => new() { [version] = _fakeExe };

    [Test]
    public void Override_wins_for_its_version()
    {
        Assert.That(RhinoLocator.ResolveRhinoExe("9", Override("9")), Is.EqualTo(_fakeExe));
        Assert.That(RhinoLocator.IsOverride("9", Override("9")), Is.True);
    }

    [Test]
    public void Override_for_9_also_serves_wip()
    {
        // GH2 tools pin "WIP"; a Rhino 9 build is the WIP, so the compat fallback applies.
        Assert.That(RhinoLocator.ResolveRhinoExe("WIP", Override("9")), Is.EqualTo(_fakeExe));
        Assert.That(RhinoLocator.IsOverride("WIP", Override("9")), Is.True);
    }

    [Test]
    public void Override_does_not_leak_to_unrelated_version()
    {
        Assert.That(RhinoLocator.IsOverride("8", Override("9")), Is.False);
    }

    [Test]
    public void Missing_override_target_is_ignored()
    {
        var overrides = new Dictionary<string, string> { ["9"] = "/does/not/exist" };
        Assert.That(RhinoLocator.IsOverride("9", overrides), Is.False);
    }

    [Test]
    public void Null_overrides_is_not_an_override()
    {
        Assert.That(RhinoLocator.IsOverride("9", null), Is.False);
    }
}
