namespace RhinoAI.ScriptProjects;

internal static class ScriptProjectRunner
{
    public static bool IsSupportedRhino => RhinoApp.Version.Major >= 9;

    private static IProjectRunner? Runner { get; set; }

    public static ReturnResult TryCreate(out IProjectRunner runner)
    {
        runner = Runner!;
        if (runner is not null)
            return ReturnResult.Success();

        try
        {
#if RHINOCODE
            runner = Runner = new RhinoCodeProjectRunner();
#else
            runner = Runner = new RhinoAppProjectRunner();
#endif
            return ReturnResult.Success();
        }
        catch (Exception ex)
        {
            return ReturnResult.Failure(ex.Message);
        }
    }

    public static ReturnResult Reload() => Runner?.Build(true) ?? ReturnResult.Failure("Runner not found");

    public static string RunScript(RhinoDoc doc, Lang lang, string script)
    {
#if RHINOCODE
        RhinoCodeRunScript runner = new ();
#else
        RhinoAppRunScript runner = new ();
#endif
        return runner.RunScript(doc, lang, script);
    }

}

