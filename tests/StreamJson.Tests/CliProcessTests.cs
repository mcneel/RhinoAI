using System;
using System.Diagnostics;
using System.IO;
using RhinoAI;

namespace RhinoAI.StreamJson.Tests;

// CliProcess resolves the launch binary from a search-path list (real File.Exists probes over temp
// files) and configures a ProcessStartInfo. The .cmd/.bat -> cmd.exe wrapping is Windows-only, so
// those assertions branch on the host OS: meaningful on the Windows CI leg, a no-op elsewhere.
[TestFixture]
public sealed class CliProcessTests
{
    private string Dir { get; set; } = string.Empty;

    [SetUp]
    public void SetUp()
    {
        Dir = Path.Combine(Path.GetTempPath(), "rhmcp-cliproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(Dir))
            Directory.Delete(Dir, recursive: true);
    }

    private string Touch(string name)
    {
        string path = Path.Combine(Dir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string Absent(string name) => Path.Combine(Dir, name);

    [Test]
    public void TryResolve_prefers_a_real_executable_over_a_cmd_shim()
    {
        string cmdShimListedFirst = Touch("claude.cmd");
        string exe = Touch("claude.exe");

        Assert.That(CliProcess.TryResolve([cmdShimListedFirst, exe], out string path), Is.True);
        Assert.That(path, Is.EqualTo(exe));
    }

    [Test]
    public void TryResolve_falls_back_to_a_cmd_shim_when_no_real_executable_exists()
    {
        string cmd = Touch("gemini.cmd");

        Assert.That(CliProcess.TryResolve([Absent("gemini.exe"), cmd], out string path), Is.True);
        Assert.That(path, Is.EqualTo(cmd));
    }

    [Test]
    public void TryResolve_keeps_search_order_among_real_executables()
    {
        string first = Touch("claude.exe");
        string second = Touch("claude.com");

        Assert.That(CliProcess.TryResolve([first, second], out string path), Is.True);
        Assert.That(path, Is.EqualTo(first));
    }

    [Test]
    public void TryResolve_is_false_when_nothing_exists()
    {
        Assert.That(CliProcess.TryResolve([Absent("claude.exe"), Absent("claude.cmd")], out string path), Is.False);
        Assert.That(path, Is.Empty);
    }

    [Test]
    public void ConfigureFileName_points_directly_at_a_real_executable()
    {
        string exe = Touch("claude.exe");
        ProcessStartInfo psi = new();

        CliProcess.ConfigureFileName(psi, exe);

        Assert.That(psi.FileName, Is.EqualTo(exe));
        Assert.That(psi.ArgumentList, Is.Empty);
    }

    [Test]
    public void ConfigureFileName_wraps_a_batch_shim_in_cmd_on_windows_only()
    {
        string cmd = Touch("claude.cmd");
        ProcessStartInfo psi = new();

        CliProcess.ConfigureFileName(psi, cmd);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(Path.GetFileName(psi.FileName), Is.EqualTo("cmd.exe").IgnoreCase);
            Assert.That(psi.ArgumentList, Is.EqualTo(new[] { "/c", cmd }));
        }
        else
        {
            Assert.That(psi.FileName, Is.EqualTo(cmd));
            Assert.That(psi.ArgumentList, Is.Empty);
        }
    }
}
