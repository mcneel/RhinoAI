using NUnit.Framework;
using Rhino.AI.Router;

namespace Rhino.AI.Router.Tests;

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

    [Test]
    public void Window_mode_defaults_to_normal_with_no_flag_no_env()
    {
        var config = RouterConfig.FromArgs([]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Normal));
    }

    [Test]
    public void Bare_hidden_flag_as_last_argument_sets_hidden()
    {
        // Regression: the old loop stopped one short of the end and dropped a
        // trailing value-less flag.
        var config = RouterConfig.FromArgs(["--default-version", "8", "--hidden"]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Hidden));
    }

    [Test]
    public void Bare_hidden_flag_followed_by_another_flag_still_sets_hidden()
    {
        var config = RouterConfig.FromArgs(["--hidden", "--default-version", "8"]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Hidden));
        Assert.That(config.DefaultVersion, Is.EqualTo("8"));
    }

    [Test]
    public void Parses_hidden_equals_minimized()
    {
        var config = RouterConfig.FromArgs(["--hidden=minimized"]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Minimized));
    }

    [Test]
    public void Parses_hidden_equals_normal()
    {
        var config = RouterConfig.FromArgs(["--hidden=normal"]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Normal));
    }

    [Test]
    public void Unrecognized_hidden_value_falls_back_to_normal()
    {
        var config = RouterConfig.FromArgs(["--hidden=nonsense"]);
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Normal));
    }

    [Test]
    public void Window_mode_env_hidden_sets_hidden_with_no_flag()
    {
        string? prev = Environment.GetEnvironmentVariable(RouterConfig.WindowModeEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, "1");
            Assert.That(RouterConfig.FromArgs([]).WindowMode, Is.EqualTo(SpawnWindowMode.Hidden));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, prev);
        }
    }

    [Test]
    public void Window_mode_env_minimized_sets_minimized_with_no_flag()
    {
        string? prev = Environment.GetEnvironmentVariable(RouterConfig.WindowModeEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, "minimized");
            Assert.That(RouterConfig.FromArgs([]).WindowMode, Is.EqualTo(SpawnWindowMode.Minimized));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, prev);
        }
    }

    [Test]
    public void Hidden_flag_wins_over_env()
    {
        string? prev = Environment.GetEnvironmentVariable(RouterConfig.WindowModeEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, "1");
            var config = RouterConfig.FromArgs(["--hidden=normal"]);
            Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Normal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RouterConfig.WindowModeEnvVar, prev);
        }
    }

    [Test]
    public void Startup_timeout_and_hidden_flag_both_apply()
    {
        var config = RouterConfig.FromArgs(["--startup-timeout", "45", "--hidden"]);
        Assert.That(config.StartupTimeoutSeconds, Is.EqualTo(45));
        Assert.That(config.WindowMode, Is.EqualTo(SpawnWindowMode.Hidden));
    }
}
