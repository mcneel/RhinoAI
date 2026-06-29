using NUnit.Framework;
using RhMcp.Router;

namespace RhMcp.Router.Tests;

[TestFixture]
public class RouterConfigTests
{
    [Test]
    public void Defaults_to_rhino_8_when_no_args()
    {
        var config = RouterConfig.FromArgs([]);
        Assert.That(config.DefaultVersion, Is.EqualTo("8"));
    }

    [TestCase("WIP")]
    [TestCase("9")]
    [TestCase("8")]
    public void Parses_default_version_long_form(string version)
    {
        var config = RouterConfig.FromArgs(["--default-version", version]);
        Assert.That(config.DefaultVersion, Is.EqualTo(version));
    }

    [Test]
    public void Parses_default_version_short_form()
    {
        var config = RouterConfig.FromArgs(["-v", "WIP"]);
        Assert.That(config.DefaultVersion, Is.EqualTo("WIP"));
    }

    [Test]
    public void Ignores_unknown_flags()
    {
        var config = RouterConfig.FromArgs(["--garbage", "value", "--default-version", "WIP"]);
        Assert.That(config.DefaultVersion, Is.EqualTo("WIP"));
    }

    [Test]
    public void Ignores_trailing_unmatched_flag()
    {
        // --default-version without a value should fall back to default.
        var config = RouterConfig.FromArgs(["--default-version"]);
        Assert.That(config.DefaultVersion, Is.EqualTo("8"));
    }
}
