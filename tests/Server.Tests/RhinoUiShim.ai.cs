namespace Rhino.UI;

// Lets the linked plug-in sources compile without this assembly ever loading RhinoCommon; both spellings exist because the localization processor rewrites LOC.STR into Localization.LocalizeString in place.
internal static class LOC
{
    public static string STR(string english) => english;
}

internal static class Localization
{
    public static string LocalizeString(string english, int contextId) => english;
}
