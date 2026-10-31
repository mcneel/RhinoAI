using Rhino.AI;
using Rhino.AI.Tools;
using Rhino.AI.Models;

namespace sdk.tests;

public class PermissionTests
{
    
    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Assert.That(AIInstances.Agents.LoadAgentsFromJson(), Is.True);
    }

    [Test, CancelAfter(5000)]
    public async Task PermissionRequested(CancellationToken token)
    {
        bool permissionRequested = false;
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        MemoryMcp mcp = new("Smoople");
        mcp.RegisterTool(new TestUtils.TestTool("Smoople", "The Smoople Tool", [])
        {
            Func = (_, _) => Task.FromResult(ToolReturn.Success("Smoopled")),
        });
        agent.Harness.AddMcp(mcp);

        throw new NotImplementedException("Below is commented out, what is going on there?");
        // agent.Harness.AskUser += async (e, __) => e.HasPermission = permissionRequested = true;

        IAgentResult result = await agent.SendAsync("Please run the Smoople tool", token);

        Assert.That(result.ToolCalls, Is.Not.Empty, "The model answered without calling a tool, so the permission gate was never reached.");
        Assert.That(permissionRequested);
    }

    [Test, CancelAfter(5000)]
    public async Task BlockedTool(CancellationToken token)
    {
        Agent? agent = Agent.GetDeepSeekAgent(Token);
        Assert.That(agent, Is.Not.Null);

        MemoryMcp mcp = new ("Smoople");
        TestUtils.TestTool tool = new ("Smoople", "The Smoople Tool", []);
        mcp.RegisterTool(tool);
        agent.Harness.AddMcp(mcp);

        Assert.That(AIInstances.Preferences.Permissions.AddPermission(new Permission(mcp, tool, Permissability.Deny, [])));

        IAgentResult result = await agent.SendAsync("Please run the Smoople tool", token);
        Assert.That(result.ToolCalls.Any(c => c.Return?.Result == ToolResult.Failure));
    }

    [TestCase(Permissability.Always)]
    [TestCase(Permissability.Ask)]
    [TestCase(Permissability.Deny)]
    public void PermissabilityNoArgs(Permissability permiss)
    {
        string mcp = "MCP";
        string tool = "tool";

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, permiss, [])));
        Assert.That(set.HasPermission(mcp, tool, []), Is.EqualTo(permiss));
    }

    [TestCase(Permissability.Always)]
    [TestCase(Permissability.Ask)]
    [TestCase(Permissability.Deny)]
    public void DefaultPermission(Permissability permiss)
    {
        string mcp = "MCP";
        string tool = "tool";

        PermissionSet set = new()
        {
            DefaultPermission = permiss
        };
        Assert.That(set.HasPermission(mcp, tool, []), Is.EqualTo(permiss));
    }

    [TestCase("*", "*")]
    [TestCase("MyMCP", "MyTool")]
    [TestCase("Rhino", "run_script")]
    public void ProhibitAllTools(string mcp, string tool)
    {
        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission("*", "*", Permissability.Deny, [])));
        Assert.That(set.HasPermission(mcp, tool, []), Is.EqualTo(Permissability.Deny));
    }

    [TestCase(Permissability.Always)]
    [TestCase(Permissability.Ask)]
    [TestCase(Permissability.Deny)]
    public void HasPermissionWithArgs(Permissability permiss)
    {
        string mcp = "MCP";
        string tool = "tool";

        Dictionary<string, ToolArgPermission> args = new()
        {
            { "arg1", new ToolArgPermission("arg1", "true") },
            { "arg2", new ToolArgPermission("arg2", "100.01") },
            { "arg3", new ToolArgPermission("arg3", "0") },
            { "arg4", new ToolArgPermission("arg4", "C:/Users/Desktop/") },
            { "arg5", new ToolArgPermission("arg5", "cheese") },
            { "arg6", new ToolArgPermission("arg6", "http://example.com") },
            { "arg7", new ToolArgPermission("arg7", "I sleep with a teddy bear") },
            { "arg8", new ToolArgPermission("arg8", "false") },
            { "arg9", new ToolArgPermission("arg2", "-20.52") },
            { "arg10", new ToolArgPermission("arg10", "-10") },
            { "arg11", new ToolArgPermission("arg11", "*") },
            { "arg12", new ToolArgPermission("arg12", "*") },
            { "arg13", new ToolArgPermission("arg13", "*") },
            { "arg14", new ToolArgPermission("arg14", "*") },
            { "arg15", new ToolArgPermission("arg15", "*") },
            { "arg16", new ToolArgPermission("arg16", "*") },
            { "arg17", new ToolArgPermission("arg17", "*") },
        };

        List<IToolArg> toolArgs = new()
        {
            new ToolBoolean("arg1", bool.Parse(args["arg1"].Value)),
            new ToolNumber("arg2", double.Parse(args["arg2"].Value)),
            new ToolInt("arg3", int.Parse(args["arg3"].Value)),
            new ToolPath("arg4", args["arg4"].Value),
            new ToolString("arg5", args["arg5"].Value),
            new ToolUrl("arg6", args["arg6"].Value),
            new ToolSecret("arg7", args["arg7"].Value),
            new ToolBoolean("arg8", bool.Parse(args["arg8"].Value)),
            new ToolNumber("arg9", double.Parse(args["arg9"].Value)),
            new ToolInt("arg10", int.Parse(args["arg10"].Value)),
            new ToolBoolean("arg11", true),
            new ToolNumber("arg12", 92.3),
            new ToolInt("arg13", -1),
            new ToolPath("arg14", "TheValue"),
            new ToolString("arg15", "TheValue"),
            new ToolUrl("arg16", "TheValue"),
            new ToolSecret("arg17", "TheValue"),
        };

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, permiss, args)));
        Assert.That(set.HasPermission(mcp, tool, toolArgs), Is.EqualTo(permiss));
    }

    [Test]
    public void MisMatchingToolArgs()
    {
        string mcp = "MCP";
        string tool = "tool";

        Dictionary<string, ToolArgPermission> args = new()
        {
            { "arg1", new ToolArgPermission("arg1", "cheese") }
        };

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, Permissability.Always, args)));
        Assert.That(set.HasPermission(mcp, tool, []), Is.EqualTo(set.DefaultPermission));
    }
    
    [Test]
    public void MisMatchingPermissionArgs()
    {
        string mcp = "MCP";
        string tool = "tool";

        List<IToolArg> toolArgs = new()
        {
            new ToolString("arg1", "cheese")
        };

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, Permissability.Always, [])));
        Assert.That(set.HasPermission(mcp, tool, toolArgs), Is.EqualTo(set.DefaultPermission));
    }

    [Test]
    public void ProhibitedTools()
    {
        string mcp = "MCP";
        string tool = "tool";

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, Permissability.Deny, [])));
        Assert.That(set.ProhibitedTools(), Is.Not.Empty);
        Assert.That(set.AllowedTools(), Is.Empty);
    }

    [Test]
    public void AllowedTools()
    {
        string mcp = "MCP";
        string tool = "tool";

        PermissionSet set = new();
        Assert.That(set.AddPermission(new Permission(mcp, tool, Permissability.Always, [])));
        Assert.That(set.AllowedTools(), Is.Not.Empty);
        Assert.That(set.ProhibitedTools(), Is.Empty);
    }

}
