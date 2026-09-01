namespace RhMcp.Router;

public sealed class PluginNotInstalledException(string version, IReadOnlyList<string> versionsWithPlugin)
    : Exception(BuildMessage(version, versionsWithPlugin))
{
    private const string SetupDocsUrl = "https://mcneel.github.io/RhinoAI/docs/getting-started/";

    public string Version { get; } = version;

    private static string BuildMessage(string version, IReadOnlyList<string> versionsWithPlugin)
    {
        string where = versionsWithPlugin.Count > 0
            ? $"It is installed for: {string.Join(", ", versionsWithPlugin)}. "
            : "No installed Rhino has it. ";
        return $"The Rhino MCP plugin is not installed for Rhino {version}. {where}" +
               $"Install it for the Rhino you want to use, then retry. Setup guide: {SetupDocsUrl}";
    }
}
