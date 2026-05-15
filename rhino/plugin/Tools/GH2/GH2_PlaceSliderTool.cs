using RhMcp.Resources;

using Eto.Drawing;

using Grasshopper2.Doc;
using Grasshopper2.Parameters.Special;
using Grasshopper2.UI;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_PlaceSliderTool
{
    public record struct SliderInfo(Guid Id, double Min, double Value, double Max, int Decimals, float X, float Y);

    [McpServerTool(Name = "place_slider")]
    [Description("Place a Number Slider on the active GH2 canvas with the given range and current value.")]
    public static string Place(
        RhinoDoc _,
        [Description("Minimum slider value.")] double min,
        [Description("Initial slider value.")] double value,
        [Description("Maximum slider value.")] double max,
        [Description("Canvas X position in pixels.")] float x = 100,
        [Description("Canvas Y position in pixels.")] float y = 100,
        [Description("Number of decimal places (0 for integer behaviour). Range: 0..12.")] int decimals = 3,
        [Description("Optional UserName for the slider.")] string? name = null,
        [Description("If true, trigger a new solution after placing. Set false to batch multiple operations and solve once at the end.")] bool solve = true)
    {
        if (decimals < 0 || decimals > 12)
            return $"Invalid decimals '{decimals}'. Valid range: 0..12.";

        if (!GH2_Utils.TryGetDoc(out Document doc))
            return "Could not get or create GH2 document";

        var number = new UiNumber(decimals, (decimal)value, (decimal)min, (decimal)max);
        var slider = new NumberSliderObject(name ?? "num", number);

        RhinoApp.InvokeAndWait(() =>
        {
            doc.Objects.Add(slider, new PointF(x, y));
            if (solve) doc.Solution.Start();
            GH2_Utils.Redraw();
        });

        var current = slider.InternalNumber;
        return JsonSerializer.Serialize(new SliderInfo(
            slider.InstanceId,
            (double)current.Lower,
            (double)current.Value,
            (double)current.Upper,
            current.Decimals,
            x,
            y));
    }
}
