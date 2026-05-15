using RhMcp.Resources;

using Grasshopper2.Doc;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_SolveTool
{
    public record struct StatusInfo(string Name, MessageItem[] Messages);
    public record struct MessageItem(string Level, string Text);

    [McpServerTool(Name = "solve_canvas")]
    [Description("Solves the active GH2 canvas. Returns per-object warning/error messages, if any.")]
    public static string SolveCanvas(RhinoDoc _)
    {
        if (!GH2_Utils.TryGetDoc(out Document ghDoc)) return "Could not get GH2 document";

        try
        {
            RhinoApp.InvokeAndWait(() =>
            {
                ghDoc.Solution.StartWait();
            });
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var statuses = CollectStatuses(ghDoc);
        if (statuses.Count == 0) return "Success";

        return JsonSerializer.Serialize(new { errMsg = "Solution encountered some errors", statuses });
    }

    private static List<StatusInfo> CollectStatuses(Document ghDoc)
    {
        var statuses = new List<StatusInfo>();
        foreach (var obj in ghDoc.Objects.ActiveObjects)
        {
            var data = obj.State?.Data;
            if (data is null) continue;
            var messages = data.Messages;
            if (messages is null || messages.Count == 0) continue;

            var items = new List<MessageItem>();
            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m.Level == Grasshopper2.Doc.MessageLevel.Remark) continue;
                items.Add(new MessageItem(m.Level.ToString(), m.Text));
            }
            if (items.Count == 0) continue;

            statuses.Add(new StatusInfo(obj.Nomen.Name, items.ToArray()));
        }
        return statuses;
    }
}
