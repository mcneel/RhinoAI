using System.Globalization;

using RhMcp.Resources;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_GetCanvasGraphTool
{
    public record struct Message(string Level, string Text);
    public record struct Endpoint(Guid Id, string Param);
    public record struct DataSummary(int Branches, int Items, string[] Sample);
    public record struct DisplaySummary(int Count, string[] Sample);
    public record struct InputInfo(string Name, string NickName, string TypeName, Endpoint[] Sources, DataSummary? Data);
    public record struct OutputInfo(string Name, string NickName, string TypeName, DataSummary? Data, DisplaySummary? DisplaySummary);
    public record struct ObjectInfo(
        Guid Id,
        string Name,
        string Kind,
        string Category,
        string SubCategory,
        float X,
        float Y,
        Message[] Messages,
        InputInfo[] Inputs,
        OutputInfo[] Outputs);
    public record struct Wire(Endpoint From, Endpoint To);
    public record struct Graph(ObjectInfo[] Objects, Wire[] Wires);

    [McpServerTool(Name = "get_canvas_graph")]
    [Description("Return a structured snapshot of the active GH1 canvas: objects (with messages, inputs/outputs and optional volatile data summaries) and wires between them.")]
    public static string GetGraph(
        RhinoDoc _,
        [Description("Include per-param volatile data summaries (branches/items/sample).")] bool include_data = true,
        [Description("How many items to include in each data sample.")] int sample_size = 3)
    {
        if (!GH1_Utils.TryGetDoc(out GH_Document doc))
            return "Could not get GH document";

        var objects = new List<ObjectInfo>();
        var wires = new List<Wire>();

        foreach (var obj in doc.Objects)
        {
            string kind = GH1_Utils.ClassifyKind(obj.GetType());

            var messages = CollectMessages(obj);
            var pivot = obj.Attributes?.Pivot ?? default;

            InputInfo[] inputs;
            OutputInfo[] outputs;

            if (obj is IGH_Component comp)
            {
                inputs = comp.Params.Input.Select(p => MakeInput(p, include_data, sample_size, wires, obj.InstanceGuid)).ToArray();
                outputs = comp.Params.Output.Select(p => MakeOutput(p, include_data, sample_size, displaySummarySource: null)).ToArray();
            }
            else if (obj is IGH_Param param)
            {
                // For a standalone param (e.g. slider), expose its own data as a single output.
                inputs = Array.Empty<InputInfo>();
                outputs = new[] { MakeOutput(param, include_data, sample_size, displaySummarySource: obj) };
            }
            else
            {
                inputs = Array.Empty<InputInfo>();
                outputs = Array.Empty<OutputInfo>();
            }

            objects.Add(new ObjectInfo(
                obj.InstanceGuid,
                obj.Name,
                kind,
                obj.Category,
                obj.SubCategory,
                pivot.X,
                pivot.Y,
                messages,
                inputs,
                outputs));
        }

        return JsonSerializer.Serialize(new Graph(objects.ToArray(), wires.ToArray()));
    }

    private static Message[] CollectMessages(IGH_DocumentObject obj)
    {
        if (obj is not IGH_ActiveObject ao) return Array.Empty<Message>();
        var list = new List<Message>();
        AddLevel(list, ao, GH_RuntimeMessageLevel.Remark);
        AddLevel(list, ao, GH_RuntimeMessageLevel.Warning);
        AddLevel(list, ao, GH_RuntimeMessageLevel.Error);
        return list.ToArray();
    }

    private static void AddLevel(List<Message> list, IGH_ActiveObject ao, GH_RuntimeMessageLevel level)
    {
        foreach (var msg in ao.RuntimeMessages(level))
            list.Add(new Message(level.ToString(), msg));
    }

    private static InputInfo MakeInput(IGH_Param param, bool includeData, int sampleSize, List<Wire> wires, Guid ownerId)
    {
        var sources = new List<Endpoint>();
        foreach (var src in param.Sources)
        {
            var top = src.Attributes?.GetTopLevel?.DocObject;
            var id = (top is null || ReferenceEquals(top, src)) ? src.InstanceGuid : top.InstanceGuid;
            var ep = new Endpoint(id, src.Name);
            sources.Add(ep);
            wires.Add(new Wire(ep, new Endpoint(ownerId, param.Name)));
        }

        return new InputInfo(
            param.Name,
            param.NickName,
            param.TypeName,
            sources.ToArray(),
            includeData ? Summarize(param, sampleSize) : null);
    }

    private static OutputInfo MakeOutput(IGH_Param param, bool includeData, int sampleSize, IGH_DocumentObject? displaySummarySource) => new(
        param.Name,
        param.NickName,
        param.TypeName,
        includeData ? Summarize(param, sampleSize) : null,
        displaySummarySource is null ? null : SummarizeDisplay(displaySummarySource, sampleSize));

    private static DisplaySummary? SummarizeDisplay(IGH_DocumentObject obj, int sampleSize)
    {
        try
        {
            if (obj is GH_NumberSlider slider)
            {
                return new DisplaySummary(1, new[] { slider.Slider.Value.ToString(CultureInfo.InvariantCulture) });
            }
            if (obj is GH_Panel panel)
            {
                var text = panel.UserText ?? "";
                if (text.Length > 200) text = text.Substring(0, 200);
                return new DisplaySummary(1, new[] { text });
            }
            if (obj is GH_ValueList list)
            {
                var sample = new List<string>();
                foreach (var item in list.ListItems)
                {
                    if (sample.Count >= sampleSize) break;
                    sample.Add(item.Name + " = " + item.Expression);
                }
                return new DisplaySummary(list.ListItems.Count, sample.ToArray());
            }
        }
        catch { }
        return null;
    }

    private static DataSummary? Summarize(IGH_Param param, int sampleSize)
    {
        IGH_Structure? data;
        try { data = param.VolatileData; }
        catch { return null; }
        if (data is null) return null;

        int branches = data.PathCount;
        int items = data.DataCount;

        var sample = new List<string>();
        foreach (var path in data.Paths)
        {
            if (sample.Count >= sampleSize) break;
            var branch = data.get_Branch(path);
            if (branch is null) continue;
            foreach (var item in branch)
            {
                if (sample.Count >= sampleSize) break;
                if (item is IGH_Goo goo)
                    sample.Add(goo?.ToString() ?? "");
                else
                    sample.Add(item?.ToString() ?? "");
            }
        }

        return new DataSummary(branches, items, sample.ToArray());
    }
}
