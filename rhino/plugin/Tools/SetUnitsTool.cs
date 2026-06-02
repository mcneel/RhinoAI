using Rhino.Geometry;
using Rhino.DocObjects;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SetUnitsTool
{
    private static readonly string[] ValidUnits =
    [
        "None", "Millimeters", "Centimeters", "Meters", "Kilometers",
        "Inches", "Feet", "Miles",
        "Microns", "Decimeters", "Dekameters", "Hectometers",
        "Megameters", "Gigameters", "NauticalMiles",
        "PrinterPoints", "PrinterPicas",
        "AstronomicalUnits", "LightYears", "Parsecs",
    ];

    [McpServerTool(Name = "set_units", Title = "Set Units", ReadOnly = false, Destructive = false)]
    [Description("Set the model unit system. Valid values: Millimeters, Centimeters, Meters, Kilometers, Inches, Feet, Miles, and others. Optionally scales existing geometry to preserve physical size.")]
    public static string SetUnits(
        RhinoDoc doc,
        [Description("Target unit system name (case-insensitive). E.g. 'Meters', 'Feet', 'Millimeters'.")] string units,
        [Description("If true, scale all existing geometry to preserve physical size. Default true.")] bool scaleGeometry = true)
    {
        if (!Enum.TryParse<UnitSystem>(units, ignoreCase: true, out var target))
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unknown unit system '{units}'. Valid: {string.Join(", ", ValidUnits)}",
            });

        var previous = doc.ModelUnitSystem;
        doc.ModelUnitSystem = target;

        if (scaleGeometry && previous != target)
        {
            var scale = RhinoMath.UnitScale(previous, target);
            var xform = Transform.Scale(Point3d.Origin, scale);
            var settings = new ObjectEnumeratorSettings { ActiveObjects = true };
            foreach (var obj in doc.Objects.GetObjectList(settings))
                doc.Objects.Transform(obj.Id, xform, true);
        }

        doc.Views.Redraw();

        return JsonSerializer.Serialize(new
        {
            success = true,
            previous = previous.ToString(),
            units = doc.ModelUnitSystem.ToString(),
            tolerance = doc.ModelAbsoluteTolerance,
        });
    }
}
