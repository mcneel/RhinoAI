using NUnit.Framework;

using Rhino.AI.Router;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class RouterStagingTests
{
    private string Root { get; set; } = string.Empty;

    private string PayloadDir => Path.Combine(Root, "payload");
    private string BinDir => Path.Combine(Root, "bin");
    private string PayloadExe => Path.Combine(PayloadDir, RouterPaths.RouterExeName);
    private string StagedExe => Path.Combine(BinDir, RouterPaths.RouterExeName);

    [SetUp]
    public void SetUp()
    {
        Root = Path.Combine(Path.GetTempPath(), $"router-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(PayloadDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private void WritePayload(string contents)
    {
        Directory.CreateDirectory(PayloadDir);
        File.WriteAllText(PayloadExe, contents);
    }

    [Test]
    public void EnsureStaged_CopiesPayloadIntoBin()
    {
        WritePayload("router-v1");

        RouterStagingResult result = RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(result.StagingError, Is.Null);
        Assert.That(result.RouterPath, Is.EqualTo(StagedExe));
        Assert.That(File.ReadAllText(StagedExe), Is.EqualTo("router-v1"));
    }

    [Test]
    public void EnsureStaged_CopiesSidecarFilesBesideTheExe()
    {
        WritePayload("router-v1");
        File.WriteAllText(Path.Combine(PayloadDir, "libe_sqlite3.dylib"), "sqlite");

        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(File.ReadAllText(Path.Combine(BinDir, "libe_sqlite3.dylib")), Is.EqualTo("sqlite"));
    }

    [Test]
    public void EnsureStaged_LeavesThePayloadInPlace()
    {
        WritePayload("router-v1");

        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(File.ReadAllText(PayloadExe), Is.EqualTo("router-v1"));
    }

    [Test]
    public void EnsureStaged_ReplacesAStaleCopy()
    {
        WritePayload("router-v1");
        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        WritePayload("router-v2-longer");
        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(File.ReadAllText(StagedExe), Is.EqualTo("router-v2-longer"));
    }

    [Test]
    public void EnsureStaged_SkipsCopyingWhenAlreadyUpToDate()
    {
        WritePayload("router-v1");
        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        DateTime stagedAt = File.GetLastWriteTimeUtc(StagedExe);
        File.WriteAllText(StagedExe, "touched-x");
        File.SetLastWriteTimeUtc(StagedExe, stagedAt);

        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(File.ReadAllText(StagedExe), Is.EqualTo("touched-x"));
    }

    [Test]
    public void EnsureStaged_KeepsTheStagedCopyWhenThePayloadDisappears()
    {
        WritePayload("router-v1");
        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Directory.Delete(PayloadDir, recursive: true);

        RouterStagingResult result = RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(result.StagingError, Is.Null);
        Assert.That(result.RouterPath, Is.EqualTo(StagedExe));
    }

    [Test]
    public void EnsureStaged_ReportsAMissingPayloadWhenNothingIsStaged()
    {
        RouterStagingResult result = RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(result.StagingError, Is.Not.Null);
        Assert.That(result.RouterPath, Is.EqualTo(PayloadExe));
    }

    [Test]
    public void EnsureStaged_SweepsLeftoversFromEarlierUpdates()
    {
        WritePayload("router-v1");
        Directory.CreateDirectory(BinDir);
        string leftover = Path.Combine(BinDir, $"{RouterPaths.RouterExeName}.deadbeef.old");
        File.WriteAllText(leftover, "stale");

        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(File.Exists(leftover), Is.False);
    }

    [Test]
    public void EnsureStaged_ReplacesAReadOnlyStagedCopy()
    {
        WritePayload("router-v1");
        RouterStaging.EnsureStaged(PayloadDir, BinDir);

        WritePayload("router-v2-longer");
        File.SetAttributes(StagedExe, FileAttributes.ReadOnly);

        RouterStagingResult result = RouterStaging.EnsureStaged(PayloadDir, BinDir);

        Assert.That(result.StagingError, Is.Null);
        Assert.That(File.ReadAllText(StagedExe), Is.EqualTo("router-v2-longer"));
    }

    [Test]
    public void EnsureStaged_PreservesTheExecutableBit()
    {
        if (!OperatingSystem.IsWindows())
        {
            WritePayload("router-v1");
            File.SetUnixFileMode(PayloadExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            RouterStaging.EnsureStaged(PayloadDir, BinDir);

            Assert.That(File.GetUnixFileMode(StagedExe).HasFlag(UnixFileMode.UserExecute), Is.True);
            return;
        }

        Assert.Ignore("Unix file modes do not apply on Windows.");
    }
}
