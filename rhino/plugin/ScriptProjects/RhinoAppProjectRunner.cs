using System.IO;

namespace RhinoAI.ScriptProjects;

internal class RhinoAppProjectRunner : IProjectRunner
{

    public ScriptProjectPaths Paths { get; }

    public RhinoAppProjectRunner()
    {
        Paths = ScriptProjectPaths.For(null) ?? throw new NullReferenceException("Paths is NULL");
    }

    private const string NOT_AVAILABLE = "Feature not available in this build";

    public ReturnResult AddCommandToProject(string commandName, string script, string? svg)
    {
        return ReturnResult.Failure(NOT_AVAILABLE);
    }

    public ReturnResult RemoveCommandFromProject(string commandName)
    {
        return ReturnResult.Failure(NOT_AVAILABLE);
    }

    public ReturnResult Build(bool reloadOnly)
    {
        return ReturnResult.Failure(NOT_AVAILABLE);
    }

}
