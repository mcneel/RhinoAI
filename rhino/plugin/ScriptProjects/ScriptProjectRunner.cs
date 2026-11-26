using Rhino.AI.Tools;

namespace Rhino.AI.ScriptProjects;

internal static class ScriptProjectRunner
{
    private static IProjectRunner? Runner { get; set; }

    public static IToolResult TryCreate(out IProjectRunner runner)
    {
        runner = Runner!;
        if (runner is not null)
            return Success();

        try
        {
            runner = Runner = new RhinoCodeProjectRunner();
            return Success();
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }
    }

    public static bool TryGetProjectCommandNames(out List<string> commandNames)
    {
        commandNames = [];
        Runner?.TryGetProjectCommandNames(out commandNames);
        return true;
    }

    public static IToolResult Reload()
    {
        IToolResult result = TryCreate(out IProjectRunner runner);
        if (result.Error is not null)
            return result;

        return runner?.Build(true) ?? Failure(ToolError.Failed, "Runner not found");
    }

    public static IToolResult RunScript(RhinoDoc doc, Lang lang, string script)
    {
        RhinoCodeRunScript runner = new ();
        return runner.RunScript(doc, lang, script);
    }

}

