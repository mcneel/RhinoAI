namespace Rhino.AI;

public sealed class DefaultToolsMcp : GenericMcp
{

    public DefaultToolsMcp(IHarness harness) : base("Default Tools")
    {
        RegisterTool(new Tools.DelegateTool());
        // RegisterTool(new Tools.Compact()); // TODO : Compaction
        RegisterTool(new Tools.ReadTool());
        RegisterTool(new Tools.WriteTool());
        RegisterTool(new Tools.EditTool());
        RegisterTool(new Tools.ReadSkill((n) => (harness.Skills.TryGetValue(n, out ISkill? skill) && skill is not null) ? skill : skill));
    }

}
