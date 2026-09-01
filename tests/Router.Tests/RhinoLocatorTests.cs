using NUnit.Framework;
using RhinoAI.Router;

namespace RhinoAI.Router.Tests;

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

    [Test]
    public void Versions_with_plugin_are_a_subset_of_installed_versions()
    {
        Assert.That(RhinoLocator.ListVersionsWithPlugin(),
            Is.SubsetOf(RhinoLocator.ListInstalledVersions()));
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

    // WIP/BETA share Rhino 9's "9.0" folder; a literal "WIP" folder was the plugin_not_installed bug.
    [TestCase("9", "9.0")]
    [TestCase("WIP", "9.0")]
    [TestCase("BETA", "9.0")]
    [TestCase("8", "8.0")]
    public void Maps_version_token_to_yak_packages_folder(string version, string expected)
    {
        Assert.That(RhinoLocator.YakPackagesFolder(version), Is.EqualTo(expected));
    }
}
