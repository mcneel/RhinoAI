using System.Drawing;

using RhMcp.Resources;

using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_PlaceSliderTool
{
    public record struct SliderInfo(Guid Id, double Min, double Value, double Max, string Type, float X, float Y);

    [McpServerTool(Name = "place_slider")]
    [Description("Place a Number Slider on the active GH1 canvas with the given range and current value. type: 'float' | 'int' | 'even' | 'odd'.")]
    public static string Place(
        RhinoDoc _,
        [Description("Minimum slider value.")] double min,
        [Description("Initial slider value.")] double value,
        [Description("Maximum slider value.")] double max,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("Slider accuracy: 'float', 'int', 'even', or 'odd'.")] string type = "float",
        [Description("Optional NickName for the slider.")] string? name = null,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (!TryParseAccuracy(type, out GH_SliderAccuracy accuracy))
            return $"Invalid type '{type}'. Valid values: 'float', 'int', 'even', 'odd'.";

        if (!GH1_Utils.TryGetOrCreateDoc(out GH_Document doc))
            return "Could not get or create GH document";

        var slider = new GH_NumberSlider();
        slider.CreateAttributes();

        slider.Slider.Minimum = (decimal)min;
        slider.Slider.Maximum = (decimal)max;
        slider.Slider.Value = (decimal)value;
        slider.Slider.Type = accuracy;

        if (!string.IsNullOrEmpty(name)) slider.NickName = name;

        slider.Attributes.Pivot = new PointF(x, y);

        RhinoApp.InvokeAndWait(() =>
        {
            doc.AddObject(slider, false);
            if (solve) doc.NewSolution(false);
            GH1_Utils.ZoomExtents();
        });

        return JsonSerializer.Serialize(new SliderInfo(
            slider.InstanceGuid,
            (double)slider.Slider.Minimum,
            (double)slider.Slider.Value,
            (double)slider.Slider.Maximum,
            FormatAccuracy(slider.Slider.Type),
            x,
            y));
    }

    private static string FormatAccuracy(GH_SliderAccuracy accuracy) => accuracy switch
    {
        GH_SliderAccuracy.Float => "float",
        GH_SliderAccuracy.Integer => "int",
        GH_SliderAccuracy.Even => "even",
        GH_SliderAccuracy.Odd => "odd",
        _ => accuracy.ToString(),
    };

    private static bool TryParseAccuracy(string type, out GH_SliderAccuracy accuracy)
    {
        switch (type?.ToLowerInvariant())
        {
            case "float":
                accuracy = GH_SliderAccuracy.Float;
                return true;
            case "int":
                accuracy = GH_SliderAccuracy.Integer;
                return true;
            case "even":
                accuracy = GH_SliderAccuracy.Even;
                return true;
            case "odd":
                accuracy = GH_SliderAccuracy.Odd;
                return true;
            default:
                accuracy = GH_SliderAccuracy.Float;
                return false;
        }
    }
}
