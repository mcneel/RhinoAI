using NUnit.Framework;

using Rhino.AI.Router;

namespace Rhino.AI.Server.Tests;

// shared/router-launcher.mjs hard-codes these same segments, so a change here must be made there too.
[TestFixture]
public class RouterPathsTests
{
    private string? PreviousHome { get; set; }

    [SetUp]
    public void SetUp() => PreviousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);

    [TearDown]
    public void TearDown() => Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, PreviousHome);

    [Test]
    public void BaseDir_AppendsTheAiDirUnderTheHomeOverride()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(), "router-paths-override");
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, overrideRoot);

        Assert.That(RouterPaths.BaseDir, Is.EqualTo(Path.Combine(overrideRoot, "ai")));
        Assert.That(RouterPaths.BinDir, Is.EqualTo(Path.Combine(overrideRoot, "ai", "bin")));
    }

    [Test]
    public void BaseDir_SitsInsideTheRhinoDirWhenNotOverridden()
    {
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, null);

        Assert.That(RouterPaths.BaseDir, Does.EndWith(Path.Combine("McNeel", "Rhinoceros", "ai")));
        Assert.That(RouterPaths.BaseDir, Does.StartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
    }

    [Test]
    public void StagedRouterExe_SitsInTheBinDir()
    {
        Assert.That(RouterPaths.StagedRouterExe, Is.EqualTo(
            Path.Combine(RouterPaths.BinDir, RouterPaths.RouterExeName)));
    }
}
