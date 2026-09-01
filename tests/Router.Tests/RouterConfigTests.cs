using NUnit.Framework;
using RhinoAI.Router;

namespace RhinoAI.Router.Tests;

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

    [Test]
    public void Default_version_arg_wins_over_env()
    {
        string? prev = Environment.GetEnvironmentVariable(RouterConfig.DefaultVersionEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(RouterConfig.DefaultVersionEnvVar, "9");
            Assert.That(RouterConfig.FromArgs([]).DefaultVersion, Is.EqualTo("9"));
            Assert.That(RouterConfig.FromArgs(["-v", "WIP"]).DefaultVersion, Is.EqualTo("WIP"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RouterConfig.DefaultVersionEnvVar, prev);
        }
    }

    [Test]
    public void No_rhino_exe_override_by_default()
    {
        var config = RouterConfig.FromArgs([]);
        Assert.That(config.RhinoExeOverrides, Is.Null);
    }

    [Test]
    public void Parses_rhino_exe_override()
    {
        var config = RouterConfig.FromArgs(["--rhino-exe", "9=/path/to/Rhinoceros.app"]);
        Assert.That(config.RhinoExeOverrides, Is.Not.Null);
        Assert.That(config.RhinoExeOverrides!["9"], Is.EqualTo("/path/to/Rhinoceros.app"));
    }

    [Test]
    public void Rhino_exe_override_splits_on_first_equals()
    {
        // A path containing '=' must survive intact.
        var config = RouterConfig.FromArgs(["--rhino-exe", "9=/path/with=equals/Rhino.exe"]);
        Assert.That(config.RhinoExeOverrides!["9"], Is.EqualTo("/path/with=equals/Rhino.exe"));
    }

    [Test]
    public void Ignores_rhino_exe_override_without_version()
    {
        var config = RouterConfig.FromArgs(["--rhino-exe", "/no/version/prefix"]);
        Assert.That(config.RhinoExeOverrides, Is.Null);
    }
}
