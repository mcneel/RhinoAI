using NUnit.Framework;
using RhMcp.Compute;

namespace RhMcp.Server.Tests;

[TestFixture]
internal class ComputeHintsTests
{
    [Test]
    public void Reachable_returns_no_hints()
    {
        var hints = ComputeHints.Build(
            ComputeHints.Windows, reachable: true,
            url: "http://localhost:6500", isCustomUrl: false, hopsInstalled: true);
        Assert.That(hints, Is.Empty);
    }

    [Test]
    public void MacOs_default_url_unreachable_suggests_remote_server()
    {
        var hints = ComputeHints.Build(
            ComputeHints.MacOs, reachable: false,
            url: "http://localhost:6500", isCustomUrl: false, hopsInstalled: false);
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Does.Contain("macOS"));
        Assert.That(hints[0], Does.Contain("RHINO_COMPUTE_URL"));
    }

    [Test]
    public void MacOs_custom_url_unreachable_uses_custom_url_hint()
    {
        var hints = ComputeHints.Build(
            ComputeHints.MacOs, reachable: false,
            url: "http://remote:6500", isCustomUrl: true, hopsInstalled: false);
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Does.Contain("http://remote:6500"));
        Assert.That(hints[0], Does.Not.Contain("macOS"));
    }

    [Test]
    public void Windows_default_url_unreachable_hops_installed_says_open_grasshopper()
    {
        var hints = ComputeHints.Build(
            ComputeHints.Windows, reachable: false,
            url: "http://localhost:6500", isCustomUrl: false, hopsInstalled: true);
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Does.Contain("Grasshopper"));
        Assert.That(hints[0], Does.Contain("Hops"));
    }

    [Test]
    public void Windows_default_url_unreachable_hops_missing_suggests_install_hops()
    {
        var hints = ComputeHints.Build(
            ComputeHints.Windows, reachable: false,
            url: "http://localhost:6500", isCustomUrl: false, hopsInstalled: false);
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Does.Contain("Install"));
        Assert.That(hints[0], Does.Contain("Hops"));
        Assert.That(hints[0], Does.Contain("Package Manager"));
    }

    [Test]
    public void Custom_url_unreachable_mentions_configured_url()
    {
        var hints = ComputeHints.Build(
            ComputeHints.Windows, reachable: false,
            url: "http://example.com:8080", isCustomUrl: true, hopsInstalled: false);
        Assert.That(hints, Has.Count.EqualTo(1));
        Assert.That(hints[0], Does.Contain("http://example.com:8080"));
        Assert.That(hints[0], Does.Contain("RHINO_COMPUTE_URL"));
    }
}
