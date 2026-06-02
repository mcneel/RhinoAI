namespace RhMcp.Tools;

[McpServerToolType]
public static class GetUnitsTool
{
    [McpServerTool(Name = "get_units", Title = "Get Units", ReadOnly = true, Destructive = false)]
    [Description("Return the current model unit system (e.g. Meters, Feet, Millimeters), absolute tolerance, and angle tolerance.")]
    public static string GetUnits(RhinoDoc doc)
    {
        return JsonSerializer.Serialize(new
        {
            units = doc.ModelUnitSystem.ToString(),
            tolerance = doc.ModelAbsoluteTolerance,
            angleTolerance = doc.ModelAngleToleranceDegrees,
        });
    }
}
