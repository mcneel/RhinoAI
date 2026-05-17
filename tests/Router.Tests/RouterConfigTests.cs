using RhMcp.Router;
using Xunit;

namespace RhMcp.Router.Tests;

public class RouterConfigTests
{
    [Fact]
    public void Defaults_to_rhino_8_when_no_args()
    {
        var config = RouterConfig.FromArgs([]);
        Assert.Equal("8", config.DefaultVersion);
    }

    [Theory]
    [InlineData("WIP")]
    [InlineData("9")]
    [InlineData("8")]
    public void Parses_default_version_long_form(string version)
    {
        var config = RouterConfig.FromArgs(["--default-version", version]);
        Assert.Equal(version, config.DefaultVersion);
    }

    [Fact]
    public void Parses_default_version_short_form()
    {
        var config = RouterConfig.FromArgs(["-v", "WIP"]);
        Assert.Equal("WIP", config.DefaultVersion);
    }

    [Fact]
    public void Ignores_unknown_flags()
    {
        var config = RouterConfig.FromArgs(["--garbage", "value", "--default-version", "WIP"]);
        Assert.Equal("WIP", config.DefaultVersion);
    }

    [Fact]
    public void Ignores_trailing_unmatched_flag()
    {
        // --default-version without a value should fall back to default.
        var config = RouterConfig.FromArgs(["--default-version"]);
        Assert.Equal("8", config.DefaultVersion);
    }
}
