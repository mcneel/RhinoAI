using Rhino.AI;
using Rhino.AI.PlugIns;

// Required for Token Registry
[assembly: System.Runtime.InteropServices.Guid("c8eb7059-28b8-482f-986e-72f404f3bbbc")]

namespace sdk.tests;

public class RegistryTests
{

    [Test]
    public void TokenPanelRegister()
    {
        PlugInToken? token = PlugInRegistry.Register();
        Assert.That(token, Is.Not.Null);

        TestSkill skill = new();
        Assert.That(PlugInRegistry.RegisterPanelSkill(token, skill));
        Assert.That(PlugInRegistry.PanelSkills, Is.Not.Empty);

        TestMcp mcp = new();
        Assert.That(PlugInRegistry.RegisterPanelMcp(token, mcp));
        Assert.That(PlugInRegistry.PanelMcps, Is.Not.Empty);

        // Duplicate registry
        Assert.That(PlugInRegistry.RegisterPanelSkill(token, skill), Is.False);
        Assert.That(PlugInRegistry.RegisterPanelMcp(token, mcp), Is.False);

        // Another Duplicate Registry
        Assert.That(PlugInRegistry.RegisterPanelSkill(token, new TestSkill()), Is.False);
        Assert.That(PlugInRegistry.RegisterPanelMcp(token, new TestMcp()), Is.False);
    }

    [Test]
    public void TestsByPassTokenPermissions()
    {
        PlugInToken? token = PlugInRegistry.Register();
        Assert.That(token, Is.Not.Null);

        Assert.That(PlugInRegistry.HasPermission(token), Is.True);
    }

    private record TestSkill : ISkill
    {
        public string Name { get; } = "Test";

        public string Description { get; } = "Description";

        public string SkillData { get; } = "Data";
    }

    private class TestMcp() : Mcp("Test")
    {
        public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
            => ToolReturn.Failure("Not implemented", "Don't use this");
    }

}
