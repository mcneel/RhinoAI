namespace Rhino.AI;

public sealed class DefaultToolsMcp : GenericMcp
{

    public DefaultToolsMcp() : base("Default Tools")
    {
        RegisterTool(new Tools.DelegateTool());
        // RegisterTool(new Tools.Compact()); // TODO : Compaction
        RegisterTool(new Tools.ReadTool());
        RegisterTool(new Tools.WriteTool());
        RegisterTool(new Tools.EditTool());
    }

}
