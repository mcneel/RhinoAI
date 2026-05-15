using RhMcp.Resources;

using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Special;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_GetCanvasGraphTool
{
    public record struct MessageInfo(string Level, string Text);
    public record struct Endpoint(Guid Id, string Param);
    public record struct DataSummary(int Branches, int Items, string[] Sample);
    public record struct DisplaySummary(int Count, string[] Sample);
    public record struct InputInfo(string Name, string UserName, string TypeName, Endpoint[] Sources, DataSummary? Data);
    public record struct OutputInfo(string Name, string UserName, string TypeName, DataSummary? Data, DisplaySummary? DisplaySummary);
    public record struct ObjectInfo(
        Guid Id,
        string Name,
        string Kind,
        string Category,
        string SubCategory,
        float X,
        float Y,
        MessageInfo[] Messages,
        InputInfo[] Inputs,
        OutputInfo[] Outputs);
    public record struct Wire(Endpoint From, Endpoint To);
    public record struct Graph(ObjectInfo[] Objects, Wire[] Wires);

    [McpServerTool(Name = "get_canvas_graph")]
    [Description("Return a structured snapshot of the active GH2 canvas: objects (with messages, inputs/outputs and optional volatile data summaries) and wires between them.")]
    public static string GetGraph(
        RhinoDoc _,
        [Description("Include per-param volatile data summaries (branches/items/sample).")] bool include_data = true,
        [Description("How many items to include in each data sample.")] int sample_size = 3)
    {
        if (!GH2_Utils.TryGetDoc(out Document doc))
            return "Could not get GH2 document";

        var objects = new List<ObjectInfo>();
        var wires = new List<Wire>();

        foreach (var obj in doc.Objects.Forwards)
        {
            string kind = GH2_Utils.ClassifyKind(obj.GetType());

            var messages = CollectMessages(obj);
            var pivot = obj.Attributes?.Pivot ?? default;

            InputInfo[] inputs;
            OutputInfo[] outputs;

            if (obj is Component comp)
            {
                inputs = comp.Parameters.Inputs.Select(p => MakeInput(doc, p, include_data, sample_size, wires, obj.InstanceId)).ToArray();
                outputs = comp.Parameters.Outputs.Select(p => MakeOutput(p, include_data, sample_size, displaySource: null)).ToArray();
            }
            else if (obj is IParameter param)
            {
                inputs = Array.Empty<InputInfo>();
                outputs = new[] { MakeOutput(param, include_data, sample_size, displaySource: obj) };
            }
            else
            {
                inputs = Array.Empty<InputInfo>();
                outputs = Array.Empty<OutputInfo>();
            }

            objects.Add(new ObjectInfo(
                obj.InstanceId,
                obj.Nomen.Name,
                kind,
                obj.Nomen.Chapter,
                obj.Nomen.Section,
                pivot.X,
                pivot.Y,
                messages,
                inputs,
                outputs));
        }

        return JsonSerializer.Serialize(new Graph(objects.ToArray(), wires.ToArray()));
    }

    private static MessageInfo[] CollectMessages(IDocumentObject obj)
    {
        var state = obj.State;
        var data = state?.Data;
        if (data is null) return Array.Empty<MessageInfo>();
        var messages = data.Messages;
        if (messages is null || messages.Count == 0) return Array.Empty<MessageInfo>();

        var list = new List<MessageInfo>(messages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            list.Add(new MessageInfo(m.Level.ToString(), m.Text));
        }
        return list.ToArray();
    }

    private static InputInfo MakeInput(Document doc, IParameter param, bool includeData, int sampleSize, List<Wire> wires, Guid ownerId)
    {
        var sources = new List<Endpoint>();
        foreach (var srcId in param.Inputs.Forwards)
        {
            var srcParam = doc.Objects.FindParameter(srcId);
            if (srcParam is null) continue;

            // Resolve to the founding object so that the wire endpoint maps to a
            // top-level canvas object rather than to a nested input parameter.
            var top = srcParam.FoundingObject ?? srcParam;
            var ep = new Endpoint(top.InstanceId, srcParam.Nomen.Name);
            sources.Add(ep);
            wires.Add(new Wire(ep, new Endpoint(ownerId, param.Nomen.Name)));
        }

        return new InputInfo(
            param.Nomen.Name,
            param.UserName ?? "",
            param.TypeAssistantWeak?.Name ?? "",
            sources.ToArray(),
            includeData ? Summarize(param, sampleSize) : null);
    }

    private static OutputInfo MakeOutput(IParameter param, bool includeData, int sampleSize, IDocumentObject? displaySource) => new(
        param.Nomen.Name,
        param.UserName ?? "",
        param.TypeAssistantWeak?.Name ?? "",
        includeData ? Summarize(param, sampleSize) : null,
        displaySource is null ? null : SummarizeDisplay(displaySource, sampleSize));

    private static DisplaySummary? SummarizeDisplay(IDocumentObject obj, int sampleSize)
    {
        try
        {
            if (obj is NumberSliderObject slider)
            {
                var n = slider.InternalNumber;
                return new DisplaySummary(1, new[] { n.FormatValue(n.Value) });
            }
        }
        catch { }
        return null;
    }

    private static DataSummary? Summarize(IParameter param, int sampleSize)
    {
        try
        {
            var data = param.State?.Data?.Tree();
            if (data is null) return null;

            int branches = data.PathCount;
            int items = data.ItemCount;

            var sample = new List<string>();
            foreach (var item in data.NonNullItems)
            {
                if (sample.Count >= sampleSize) break;
                sample.Add(item?.ToString() ?? "");
            }

            return new DataSummary(branches, items, sample.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
