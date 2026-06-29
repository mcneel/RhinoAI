using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

[TestFixture]
public class RhinoLocatorTests
{
    // Regression for the conflated token-to-install mapping: the tokens the
    // locator advertises must be exactly the ones the header documents ("8",
    // "9", "WIP") and nothing else. Previously ListInstalledVersions probed
    // undocumented "10"/"11"/"12" that no branch could ever resolve. Compared
    // as a set so the assertion pins the token membership, not Dictionary
    // insertion-order preservation (not a documented CLR guarantee).
    [Test]
    public void Advertises_exactly_the_documented_version_tokens()
    {
        Assert.That(RhinoLocator.KnownVersionTokens, Is.EquivalentTo(new[] { "8", "9", "WIP" }));
    }

    // "9" and "WIP" are deliberate aliases for the current WIP install; the
    // documented "8" token is distinct. This pins the aliasing so a future
    // edit can't silently make "9" mean a non-WIP install on one platform only.
    [Test]
    public void Treats_nine_and_WIP_as_the_same_install_token_distinct_from_eight()
    {
        Assert.That(RhinoLocator.KnownVersionTokens, Does.Contain("9"));
        Assert.That(RhinoLocator.KnownVersionTokens, Does.Contain("WIP"));
        Assert.That(RhinoLocator.KnownVersionTokens, Does.Contain("8"));
    }
}
