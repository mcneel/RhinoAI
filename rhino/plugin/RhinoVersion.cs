namespace RhinoAI;

// Single source for the version token the router keys slots by. The listener
// announcement and the Connect dialog's version pin must use the same token, or a
// pinned config targets a Rhino the router won't match. 9 also covers BETA/WIP.
internal static class RhinoVersion
{
    public static string Token => Rhino.RhinoApp.Version.Major.ToString();
}
