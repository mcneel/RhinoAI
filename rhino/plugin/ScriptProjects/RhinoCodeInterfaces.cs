
namespace RhinoAI.ScriptProjects;

internal interface IRhinoCodeRunner
{
    

    public string RunScript(RhinoDoc doc, Lang lang, string script);


}

public enum Lang { Python3, CSharp }


internal interface IProjectRunner
{
    
    public ScriptProjectPaths Paths { get; }

    public ReturnResult AddCommandToProject(string commandName, string script, string? svg);

    public ReturnResult RemoveCommandFromProject(string commandName);

    public ReturnResult Build(bool reloadOnly);

}
