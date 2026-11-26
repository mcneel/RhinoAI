using System.Collections.Generic;
using NUnit.Framework;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class McpYamlEditTests
{
    private const string Server = "rhino";
    private const string Command = "/opt/rhino/router/rhino-mcp-router";
    private const McpConfigFormat Yaml = McpConfigFormat.HermesYaml;

    private static Dictionary<string, string> NoEnv => new();

    private static string RhinoEntry(string indent) =>
        $"{indent}rhino:\n" +
        $"{indent}  command: \"{Command}\"\n" +
        $"{indent}  args: []\n";

    [Test]
    public void Add_EmptyFile_CreatesContainer()
    {
        string result = McpConfigEdit.Add(Yaml, "", Server, Command, NoEnv)!;

        Assert.That(result, Is.EqualTo("mcp_servers:\n" + RhinoEntry("  ")));
        Assert.That(McpConfigEdit.HasEntry(Yaml, result, Server), Is.True);
    }

    [Test]
    public void Add_MissingContainer_AppendsAfterExistingKeysAndKeepsComments()
    {
        string original = "# my settings\nmodel: x  # pinned\n";

        string result = McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv)!;

        Assert.That(result, Is.EqualTo(original + "\nmcp_servers:\n" + RhinoEntry("  ")));
    }

    [Test]
    public void Add_ExistingBlock_InsertsAtSiblingIndentAndKeepsSiblings()
    {
        string original =
            "mcp_servers:\n" +
            "    github:\n" +
            "        command: \"npx\"\n" +
            "model: x\n";

        string result = McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv)!;

        Assert.That(result, Is.EqualTo(
            "mcp_servers:\n" +
            RhinoEntry("    ") +
            "    github:\n" +
            "        command: \"npx\"\n" +
            "model: x\n"));
        Assert.That(McpConfigEdit.HasEntry(Yaml, result, "github"), Is.True);
    }

    [TestCase("mcp_servers: {}\nmodel: x\n")]
    [TestCase("mcp_servers: null\nmodel: x\n")]
    [TestCase("mcp_servers:\nmodel: x\n")]
    public void Add_EmptyContainer_WritesBlock(string original)
    {
        string result = McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv)!;

        Assert.That(result, Is.EqualTo("mcp_servers:\n" + RhinoEntry("  ") + "model: x\n"));
    }

    [Test]
    public void Add_WritesEnvAndEscapesWindowsPath()
    {
        Dictionary<string, string> env = new() { ["RHINO_MCP_DEFAULT_VERSION"] = "9" };

        string result = McpConfigEdit.Add(Yaml, "", Server, @"C:\Program Files\Rhino\router.exe", env)!;

        Assert.That(result, Is.EqualTo(
            "mcp_servers:\n" +
            "  rhino:\n" +
            "    command: \"C:\\\\Program Files\\\\Rhino\\\\router.exe\"\n" +
            "    args: []\n" +
            "    env:\n" +
            "      RHINO_MCP_DEFAULT_VERSION: \"9\"\n"));
    }

    [TestCase("mcp_servers:\n  rhino:\n    command: \"old\"\n")]
    [TestCase("mcp_servers:\n  \"rhino\":\n    command: \"old\"\n")]
    public void Add_ReturnsNullWhenAlreadyPresent(string original)
    {
        Assert.That(McpConfigEdit.HasEntry(Yaml, original, Server), Is.True);
        Assert.That(McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv), Is.Null);
    }

    [TestCase("mcp_servers: { github: { command: npx } }\n")]
    [TestCase("mcp_servers:\n  - name: github\n")]
    [TestCase("mcp_servers:\n- name: github\n")]
    [TestCase("mcp_servers: &servers\n  github:\n    command: npx\n")]
    public void Add_ReturnsNullForShapesItCannotEdit(string original)
    {
        Assert.That(McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv), Is.Null);
    }

    [TestCase("mcp_servers:\n  other:\n    env:\n      rhino: x\n")]
    [TestCase("mcp_servers:\n  rhinoceros:\n    command: x\n")]
    [TestCase("model:\n  rhino: x\n")]
    public void HasEntry_IgnoresKeysThatAreNotServers(string content)
    {
        Assert.That(McpConfigEdit.HasEntry(Yaml, content, Server), Is.False);
    }

    [Test]
    public void Remove_DropsEntryAndNestedLinesKeepingSiblingsAndComments()
    {
        string original =
            "# top\n" +
            "mcp_servers:\n" +
            "  # github server\n" +
            "  github:\n" +
            "    command: \"npx\"\n" +
            "\n" +
            "  rhino:\n" +
            "    command: \"/p\"\n" +
            "# stray\n" +
            "    env:\n" +
            "      A: \"1\"\n" +
            "\n" +
            "  other:\n" +
            "    command: \"y\"\n" +
            "model: x\n";

        string result = McpConfigEdit.Remove(Yaml, original, Server)!;

        Assert.That(result, Is.EqualTo(
            "# top\n" +
            "mcp_servers:\n" +
            "  # github server\n" +
            "  github:\n" +
            "    command: \"npx\"\n" +
            "\n" +
            "  other:\n" +
            "    command: \"y\"\n" +
            "model: x\n"));
    }

    [Test]
    public void Remove_LastEntry_LeavesEmptyMap()
    {
        string original = "mcp_servers:\n  rhino:\n    command: \"/p\"\nmodel: x\n";

        string result = McpConfigEdit.Remove(Yaml, original, Server)!;

        Assert.That(result, Is.EqualTo("mcp_servers: {}\nmodel: x\n"));
        Assert.That(McpConfigEdit.Add(Yaml, result, Server, Command, NoEnv), Is.EqualTo("mcp_servers:\n" + RhinoEntry("  ") + "model: x\n"));
    }

    [Test]
    public void Remove_EntryAbsent_ReturnsOriginal()
    {
        string original = "mcp_servers:\n  github:\n    command: npx\n";

        Assert.That(McpConfigEdit.Remove(Yaml, original, Server), Is.EqualTo(original));
    }

    [Test]
    public void AddThenRemove_PreservesCrLf()
    {
        string original = "model: x\r\nmcp_servers:\r\n  github:\r\n    command: npx\r\n";

        string added = McpConfigEdit.Add(Yaml, original, Server, Command, NoEnv)!;
        string removed = McpConfigEdit.Remove(Yaml, added, Server)!;

        Assert.That(added, Does.Not.Match("[^\r]\n"));
        Assert.That(removed, Is.EqualTo(original));
    }
}
