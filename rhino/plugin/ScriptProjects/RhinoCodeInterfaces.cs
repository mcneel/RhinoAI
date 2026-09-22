using Rhino.AI.Tools;

namespace Rhino.AI.ScriptProjects;

internal interface IRhinoCodeRunner
{
    

    public IToolResult RunScript(RhinoDoc doc, Lang lang, string script);


}

internal enum Lang { Python3, CSharp }


internal interface IProjectRunner
{
    
    public ScriptProjectPaths Paths { get; }

    public IToolResult AddCommandToProject(string commandName, string script, string? svg);

    public IToolResult RemoveCommandFromProject(string commandName);

    public IToolResult Build(bool reloadOnly);

    public bool TryGetProjectCommandNames(out List<string> commandNames);

}
