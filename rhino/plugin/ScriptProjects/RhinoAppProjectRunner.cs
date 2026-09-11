using System.IO;

using Rhino.AI.Tools;

namespace Rhino.AI.ScriptProjects;

internal class RhinoAppProjectRunner : IProjectRunner
{

    public ScriptProjectPaths Paths { get; }

    public RhinoAppProjectRunner()
    {
        Paths = ScriptProjectPaths.For(null);
    }

    private const string NOT_AVAILABLE = "Feature not available in this build";

    public IToolResult AddCommandToProject(string commandName, string script, string? svg)
    {
        return Failure(ToolError.Unsupported, NOT_AVAILABLE);
    }

    public IToolResult RemoveCommandFromProject(string commandName)
    {
        return Failure(ToolError.Unsupported, NOT_AVAILABLE);
    }

    public IToolResult Build(bool reloadOnly)
    {
        return Failure(ToolError.Unsupported, NOT_AVAILABLE);
    }

    public bool TryGetProjectCommandNames(out List<string> commandNames)
    {
        commandNames = [];
        return false;
    }

}
