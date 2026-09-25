namespace Rhino.AI;

/// <summary>
/// A simple default mcp with all the basic tools
/// </summary>
public sealed class DefaultToolsMcp : GenericMcp
{

    /// <summary>
    /// Creates a default MCP for the given Harness
    /// </summary>
    /// <param name="harness">The harness</param>
    public DefaultToolsMcp(IHarness harness, PlugIns.PlugInToken token) : base("Default Tools")
    {
        RegisterTool(new Tools.DelegateTool(token));
        // RegisterTool(new Tools.Compact()); // TODO : Compaction
        RegisterTool(new Tools.ReadTool());
        RegisterTool(new Tools.WriteTool());
        RegisterTool(new Tools.EditTool());
        RegisterTool(new Tools.ReadSkill((n) => (harness.Skills.TryGetValue(n, out ISkill? skill) && skill is not null) ? skill : skill));
    }

}
