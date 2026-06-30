namespace RhMcp.Tools;

[McpServerToolType]
public static class GetSelectionTool
{
    [McpServerTool("get_selection", "Get Selection", true, false)]
    [Description("Return all currently selected objects in Rhino.")]
    public static string GetSelection(RhinoDoc doc) =>
        JsonSerializer.Serialize(GetContextTool.SelectionOf(doc), McpSerializer.Options);
}
