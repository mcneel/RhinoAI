namespace RhMcp.Router;

public static class VersionMatch
{
    // "9", "BETA", and "WIP" are the same Rhino 9 family: a user-opened build of
    // any of them announces as "9", so all three are mutually compatible.
    private static readonly HashSet<string> Rhino9Family = new() { "9", "BETA", "WIP" };

    public static bool IsCompatible(string actual, string required)
    {
        if (actual == required)
            return true;
        return Rhino9Family.Contains(actual) && Rhino9Family.Contains(required);
    }
}
