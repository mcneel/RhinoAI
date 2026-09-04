using System.Text.RegularExpressions;

namespace RhinoAI.ScriptProjects;

internal static class PluginNameResolver
{
    public static string Resolve(string? requestedPluginName)
    {
        if (PluginNaming.TrySanitisePluginName(requestedPluginName, out string requested))
            return requested;

        if (PluginNaming.TrySanitisePluginName(AISettings.ScriptPluginName, out string configured))
            return configured;

        return PluginNaming.PluginNameFor(LoggedInUserName() ?? Environment.UserName);
    }

    private static string? LoggedInUserName()
    {
        try
        {
            Match match = Regex.Match(RhinoApp.LoggedInUserName, @"([\S\s]+) - [\S]+@");
            string userName = match.Groups[1].Value;
            return string.IsNullOrWhiteSpace(userName) ? null : userName;
        }
        catch
        {
            return null;
        }
    }
}
