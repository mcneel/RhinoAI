using NUnit.Framework;

namespace RhMcp.Server.Tests;

[TestFixture]
public class ScriptGuardTests
{
    private const string CleanPythonScript = """
        import rhinoscriptsyntax as rs
        doc = __rhino_doc__
        sphere_id = doc.Objects.AddSphere(Rhino.Geometry.Sphere(Rhino.Geometry.Point3d.Origin, 5.0))
        doc.Views.Redraw()
        """;

    private const string CleanCSharpScript = """
        using Rhino.Geometry;
        var doc = __rhino_doc__;
        var sphere = new Sphere(Point3d.Origin, 5.0);
        doc.Objects.AddSphere(sphere);
        doc.Views.Redraw();
        """;

    [Test]
    public void CleanScripts_MatchNothing()
    {
        foreach (ScriptRail rail in Enum.GetValues<ScriptRail>())
        {
            Assert.That(ScriptGuard.ScanScript(rail, CleanPythonScript), Is.Empty, $"python, {rail}");
            Assert.That(ScriptGuard.ScanScript(rail, CleanCSharpScript), Is.Empty, $"csharp, {rail}");
        }
    }

    [TestCase("System.IO.File.Delete(path);", "File.Delete")]
    [TestCase("File.Move(a, b);", "File.Move")]
    [TestCase("using var w = new StreamWriter(path);", "StreamWriter")]
    [TestCase("doc.WriteFile(path, options);", "WriteFile")]
    [TestCase("os.remove(path)", "os.remove")]
    [TestCase("import shutil\nshutil.rmtree(d)", "shutil.")]
    [TestCase("from pathlib import Path\nPath(p).write_text('x')", ".write_text(")]
    [TestCase("f = open(p, 'w')", "open( with a write mode")]
    [TestCase("f = open(p, \"ab\")", "open( with a write mode")]
    [TestCase("f = open(p, 'r+')", "open( with a write mode")]
    public void FileWrite_Matches(string source, string expected)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.FileWrite, source), Does.Contain(expected));
    }

    [TestCase("f = open(p)")]
    [TestCase("f = open(p, 'r')")]
    [TestCase("f = open(p, 'rb')")]
    public void FileWrite_IgnoresReadModeOpen(string source)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.FileWrite, source), Is.Empty);
    }

    [TestCase("f = open(p)", "open(")]
    [TestCase("text = File.ReadAllText(p);", "File.ReadAll")]
    [TestCase("using System.IO;", "System.IO")]
    [TestCase("for f in os.listdir(d):", "os.listdir")]
    [TestCase("p = os.path.join(a, b)", "os.path.")]
    public void FileAccess_MatchesReads(string source, string expected)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.FileAccess, source), Does.Contain(expected));
    }

    [TestCase("var client = new HttpClient();", "HttpClient")]
    [TestCase("import urllib.request", "urllib")]
    [TestCase("import requests", "import requests")]
    [TestCase("r = requests.post(url, data=payload)", "requests.")]
    [TestCase("import socket\ns = socket.socket()", "socket.")]
    [TestCase("from urllib.request import urlopen\nurlopen(url)", "urllib")]
    public void Network_Matches(string source, string expected)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.Network, source), Does.Contain(expected));
    }

    [Test]
    public void Network_IgnoresProseAboutRequests()
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.Network, "# handles geometry requests from the doc"), Is.Empty);
    }

    [TestCase("subprocess.run(['rm', '-rf', d])", "subprocess")]
    [TestCase("os.system('curl example.com')", "os.system")]
    [TestCase("Process.Start(\"cmd.exe\");", "Process.Start")]
    public void ProcessLaunch_Matches(string source, string expected)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.ProcessLaunch, source), Does.Contain(expected));
    }

    [TestCase("key = os.environ['API_KEY']", "os.environ")]
    [TestCase("token = os.getenv('TOKEN')", "os.getenv")]
    [TestCase("var v = Environment.GetEnvironmentVariable(\"PATH\");", "Environment.GetEnvironmentVariable")]
    public void EnvironmentRead_Matches(string source, string expected)
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.EnvironmentRead, source), Does.Contain(expected));
    }

    [TestCase("_-Export \"C:\\out.stl\" _Enter", "export")]
    [TestCase("!_SaveAs \"C:\\copy.3dm\"", "saveas")]
    [TestCase("-ViewCaptureToFile \"C:\\shot.png\" _Enter", "viewcapturetofile")]
    public void FileWriteCommands_Match(string command, string expected)
    {
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileWrite, command), Does.Contain(expected));
    }

    [Test]
    public void SaveAs_ReportsSaveAsNotSave()
    {
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileWrite, "_SaveAs"), Is.EqualTo(new[] { "saveas" }));
    }

    [TestCase("_Box 0,0,0 10,10,10 _Enter")]
    [TestCase("_Line 0,0,0 5,5,0")]
    [TestCase("_SelAll _Delete")]
    public void InnocuousCommands_MatchNothing(string command)
    {
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileWrite, command), Is.Empty);
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileAccess, command), Is.Empty);
        Assert.That(ScriptGuard.ScanCommandForScriptEscapes(command), Is.Empty);
    }

    [Test]
    public void OpenCommand_MatchesFileAccessOnly()
    {
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileAccess, "_-Open \"C:\\model.3dm\""), Does.Contain("open"));
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileWrite, "_-Open \"C:\\model.3dm\""), Is.Empty);
    }

    [TestCase("-_RunPythonScript \"C:\\evil.py\"", "runpythonscript")]
    [TestCase("-ScriptEditor _Run \"C:\\x.py\"", "scripteditor")]
    [TestCase("_ReadCommandFile \"C:\\cmds.txt\"", "readcommandfile")]
    public void ScriptEscapeCommands_Match(string command, string expected)
    {
        Assert.That(ScriptGuard.ScanCommandForScriptEscapes(command), Does.Contain(expected));
    }

    [Test]
    public void EmptyInput_MatchesNothing()
    {
        Assert.That(ScriptGuard.ScanScript(ScriptRail.Network, string.Empty), Is.Empty);
        Assert.That(ScriptGuard.ScanCommand(ScriptRail.FileWrite, string.Empty), Is.Empty);
        Assert.That(ScriptGuard.ScanCommandForScriptEscapes(string.Empty), Is.Empty);
    }
}
