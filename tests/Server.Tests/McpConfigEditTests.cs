using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace RhMcp.Server.Tests;

[TestFixture]
public class McpConfigEditTests
{
    private const string Server = "rhino";
    private const string Command = "/opt/rhino/router/rhino-mcp-router";

    private static Dictionary<string, string> NoEnv => new();
    private static Dictionary<string, string> Env => new() { ["RHINO_MCP_DEFAULT_VERSION"] = "9" };

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Test]
    public void StandardJson_Add_InsertsRhinoAndKeepsSiblings()
    {
        string original = """{ "mcpServers": { "other": { "command": "x" } } }""";

        string? result = McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, NoEnv);

        Assert.That(result, Is.Not.Null);
        JsonNode node = Parse(result!);
        Assert.That(node["mcpServers"]!["rhino"]!["command"]!.GetValue<string>(), Is.EqualTo(Command));
        Assert.That(node["mcpServers"]!["other"], Is.Not.Null);
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.StandardJson, result!, Server), Is.True);
    }

    [Test]
    public void StandardJson_Add_CreatesContainerWhenMissing()
    {
        string original = """{ "otherKey": 1 }""";

        string? result = McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, NoEnv);

        Assert.That(result, Is.Not.Null);
        JsonNode node = Parse(result!);
        Assert.That(node["mcpServers"]!["rhino"]!["command"]!.GetValue<string>(), Is.EqualTo(Command));
        Assert.That(node["otherKey"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void StandardJson_Add_WritesEnvBlock()
    {
        string original = """{ "mcpServers": {} }""";

        string? result = McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, Env);

        JsonNode node = Parse(result!);
        Assert.That(node["mcpServers"]!["rhino"]!["env"]!["RHINO_MCP_DEFAULT_VERSION"]!.GetValue<string>(), Is.EqualTo("9"));
    }

    [Test]
    public void StandardJson_Add_ReturnsNullWhenAlreadyPresent()
    {
        string original = """{ "mcpServers": { "rhino": { "command": "old" } } }""";

        Assert.That(McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, NoEnv), Is.Null);
    }

    [Test]
    public void StandardJson_Add_ReturnsNullWhenContainerIsNotAnObject()
    {
        string original = """{ "mcpServers": [] }""";

        Assert.That(McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, NoEnv), Is.Null);
    }

    [Test]
    public void StandardJson_Remove_DropsRhinoAndKeepsSiblings()
    {
        string original = """{ "mcpServers": { "rhino": { "command": "x" }, "other": { "command": "y" } } }""";

        string? result = McpConfigEdit.Remove(McpConfigFormat.StandardJson, original, Server);

        Assert.That(result, Is.Not.Null);
        JsonNode node = Parse(result!);
        Assert.That(node["mcpServers"]!["rhino"], Is.Null);
        Assert.That(node["mcpServers"]!["other"], Is.Not.Null);
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.StandardJson, result!, Server), Is.False);
    }

    [Test]
    public void StandardJson_Remove_ReturnsNullWhenContainerMissing()
    {
        Assert.That(McpConfigEdit.Remove(McpConfigFormat.StandardJson, """{ "foo": 1 }""", Server), Is.Null);
    }

    [Test]
    public void StandardJson_AddThenRemove_LeavesSiblingsWithoutRhino()
    {
        string original = """{ "mcpServers": { "other": { "command": "y" } } }""";

        string added = McpConfigEdit.Add(McpConfigFormat.StandardJson, original, Server, Command, Env)!;
        string removed = McpConfigEdit.Remove(McpConfigFormat.StandardJson, added, Server)!;

        JsonNode node = Parse(removed);
        Assert.That(node["mcpServers"]!["other"], Is.Not.Null);
        Assert.That(node["mcpServers"]!["rhino"], Is.Null);
    }

    [Test]
    public void OpenCode_Add_WritesLocalEntry()
    {
        string original = """{ "mcp": {} }""";

        string result = McpConfigEdit.Add(McpConfigFormat.OpenCodeJson, original, Server, Command, NoEnv)!;

        JsonNode node = Parse(result);
        Assert.That(node["mcp"]!["rhino"]!["type"]!.GetValue<string>(), Is.EqualTo("local"));
        Assert.That(node["mcp"]!["rhino"]!["command"]![0]!.GetValue<string>(), Is.EqualTo(Command));
        Assert.That(node["mcp"]!["rhino"]!["enabled"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void OpenCode_Add_UsesEnvironmentKeyForEnv()
    {
        string original = """{ "mcp": {} }""";

        string result = McpConfigEdit.Add(McpConfigFormat.OpenCodeJson, original, Server, Command, Env)!;

        JsonNode node = Parse(result);
        Assert.That(node["mcp"]!["rhino"]!["environment"]!["RHINO_MCP_DEFAULT_VERSION"]!.GetValue<string>(), Is.EqualTo("9"));
    }

    [Test]
    public void OpenCode_Remove_DropsRhino()
    {
        string original = """{ "mcp": { "rhino": { "type": "local" }, "other": {} } }""";

        string result = McpConfigEdit.Remove(McpConfigFormat.OpenCodeJson, original, Server)!;

        JsonNode node = Parse(result);
        Assert.That(node["mcp"]!["rhino"], Is.Null);
        Assert.That(node["mcp"]!["other"], Is.Not.Null);
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.OpenCodeJson, result, Server), Is.False);
    }

    [Test]
    public void Toml_Add_AppendsTableAndKeepsExisting()
    {
        string original = "[existing]\nkey = 1\n";

        string result = McpConfigEdit.Add(McpConfigFormat.CodexToml, original, Server, Command, NoEnv)!;

        Assert.That(result, Does.Contain("[mcp_servers.rhino]"));
        Assert.That(result, Does.Contain("command = "));
        Assert.That(result, Does.Contain("[existing]"));
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, result, Server), Is.True);
    }

    [Test]
    public void Toml_Add_AppendsEnvSubtable()
    {
        string result = McpConfigEdit.Add(McpConfigFormat.CodexToml, "", Server, Command, Env)!;

        Assert.That(result, Does.Contain("[mcp_servers.rhino.env]"));
        Assert.That(result, Does.Contain("RHINO_MCP_DEFAULT_VERSION = \"9\""));
    }

    [Test]
    public void Toml_Add_ReturnsNullWhenAlreadyPresent()
    {
        string original = "[mcp_servers.rhino]\ncommand = \"x\"\n";

        Assert.That(McpConfigEdit.Add(McpConfigFormat.CodexToml, original, Server, Command, NoEnv), Is.Null);
    }

    [Test]
    public void Toml_Remove_DropsTableAndEnvSubtableLeavingNoOrphan()
    {
        string original =
            "[foo]\nx = true\n\n" +
            "[mcp_servers.rhino]\ncommand = \"/p\"\n\n" +
            "[mcp_servers.rhino.env]\nRHINO_MCP_DEFAULT_VERSION = \"9\"\n\n" +
            "[bar]\ny = 2\n";

        string result = McpConfigEdit.Remove(McpConfigFormat.CodexToml, original, Server)!;

        Assert.That(result, Does.Not.Contain("mcp_servers.rhino"));
        Assert.That(result, Does.Contain("[foo]"));
        Assert.That(result, Does.Contain("[bar]"));
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, result, Server), Is.False);
    }

    [Test]
    public void Toml_Remove_KeepsUnrelatedServerWithSimilarName()
    {
        string original = "[mcp_servers.rhinoceros]\ncommand = \"z\"\n";

        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, original, Server), Is.False);

        string result = McpConfigEdit.Remove(McpConfigFormat.CodexToml, original, Server)!;
        Assert.That(result, Does.Contain("rhinoceros"));
    }

    [Test]
    public void Toml_HasEntryAndRemove_HandleQuotedKey()
    {
        string original = "[mcp_servers.\"rhino\"]\ncommand = \"x\"\n";

        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, original, Server), Is.True);

        string result = McpConfigEdit.Remove(McpConfigFormat.CodexToml, original, Server)!;
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, result, Server), Is.False);
    }

    [Test]
    public void Toml_AddThenRemove_WithEnv_KeepsUnrelatedTable()
    {
        string original = "[keep]\na = 1\n";

        string added = McpConfigEdit.Add(McpConfigFormat.CodexToml, original, Server, Command, Env)!;
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, added, Server), Is.True);

        string removed = McpConfigEdit.Remove(McpConfigFormat.CodexToml, added, Server)!;
        Assert.That(McpConfigEdit.HasEntry(McpConfigFormat.CodexToml, removed, Server), Is.False);
        Assert.That(removed, Does.Contain("[keep]"));
    }
}
