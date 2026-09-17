using System.Text.RegularExpressions;

namespace Rhino.AI;

// Whether a script will stop and ask the user for something, and for what. Ported from
// ScriptEditorChat, where it drove the status line; here it is what the permission card says a run is
// about to do, so "allow" is an informed answer.
//
// The getter names are curated rather than matched by prefix, so non-interactive lookalikes
// (GetType, GetBoundingBox, GetUserString) do not read as a prompt.
internal static class ScriptInputs
{
    private static Regex Objects { get; } = new(
        @"\bGet(Objects?|ObjectEx|ObjectsEx|CurveObjects?|SurfaceObjects?|PolysurfaceObjects?|MeshObjects?|EdgeCurves|MeshFaces|OneObject|MultipleObjects|SubObjects?)\s*\(",
        RegexOptions.Compiled);

    private static Regex Points { get; } = new(
        @"\bGet(Points?|PointOnCurve|PointOnSurface|PointOnMesh|PointCoordinates)\s*\(",
        RegexOptions.Compiled);

    private static Regex Values { get; } = new(
        @"\b(Get(String|Integer|Real|Number|Distance|Angle|Boolean|Bool|Color|Line|Rectangle|Polyline|Box|Option|Layer|Layers)|RhinoGet)\s*\(|\binput\s*\(",
        RegexOptions.Compiled);

    /// <summary>What the script will ask the user for, or null when it runs without stopping.</summary>
    public static string? Describe(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        if (Objects.IsMatch(code))
            return "It will stop and ask you to select objects in a viewport.";

        if (Points.IsMatch(code))
            return "It will stop and ask you to pick a point in a viewport.";

        if (Values.IsMatch(code))
            return "It will stop and ask for input on Rhino's command line.";

        return null;
    }
}
