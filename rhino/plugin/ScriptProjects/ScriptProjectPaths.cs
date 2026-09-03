using System.IO;

namespace RhinoAI.ScriptProjects;

internal sealed record ScriptProjectPaths(string PluginName, string Directory, string BuildDirectory)
{
    public static ScriptProjectPaths For(string? requestedPluginName)
    {
        string pluginName = PluginNameResolver.Resolve(requestedPluginName);

        string root = RhinoApp.GetDataDirectory(
            localUser: true,
            forceDirectoryCreation: false,
            subDirectory: Path.Combine("RhinoAI", "Projects", pluginName));

        return new ScriptProjectPaths(
            pluginName,
            root,
            Path.Combine(root, "build", $"rh{RhinoApp.Version.Major}"));
    }

    public string ProjectFile => Path.Combine(Directory, PluginName + ".rhproj");

    public string BuiltPlugin => Path.Combine(BuildDirectory, PluginName + ".rhp");

    public bool HasBuild => File.Exists(BuiltPlugin);

}
