using System.IO;

using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Storage;
using Rhino.Runtime.Code.Projects;
using Rhino.Runtime.Code.Platform;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;

namespace RhinoAI.ScriptProjects;

internal class ScriptProjectRunner
{
    public static bool IsSupportedRhino => RhinoApp.Version.Major >= 9;

    private IProject? CachedProject { get; set; }

    public ScriptProjectPaths Paths { get; }

    private Host Host { get; } = new("Rhino3D", new HostVersion(RhinoApp.Version));

    private IProgress<ProgressReport> Reporter { get; } = new SilentProgressReporter();

    public IEnumerable<string> Commands => CachedProject?.GetCodes().Select(c => c.Title) ?? [];


    private ScriptProjectRunner()
    {
        Paths = ScriptProjectPaths.For(null) ?? throw new NullReferenceException("Paths is NULL");
        Paths.Directory.EnsureDirectory();
    }

    private static ScriptProjectRunner? Runner { get; set; }

    public static ReturnResult TryCreate(out ScriptProjectRunner runner)
    {
        runner = Runner!;
        if (runner is not null)
            return ReturnResult.Success();

        try
        {
            runner = Runner = new();
            return ReturnResult.Success();
        }
        catch (Exception ex)
        {
            return ReturnResult.Failure(ex.Message);
        }
    }

    private ReturnResult TryGetProject(out IProject project)
    {
        project = default!;

        if (CachedProject is null)
        {
            Uri projectFilePath = new(Paths.ProjectFile);

            if (File.Exists(projectFilePath.LocalPath))
            {
                RhinoCode.ProjectServers.TryCreateProject(projectFilePath, out project);
            }
            else
            {
                IProjectServer? server = RhinoCode.ProjectServers
                        .WherePasses(new ProjectServerSpec("mcneel.rhino3d.project"))
                        .FirstOrDefault();

                if (server is null)
                    return ReturnResult.Failure("Could not get Project");

                // Identity and settings
                project = server.CreateProject();
                project.Identity.Name = Paths.PluginName;
                project.Identity.Tags = [Paths.PluginName];
                project.Identity.Publisher = GetPublisherIdentity();
                project.Identity.Copyright = project.Identity.Publisher.Name;
                project.Identity.Version = ProjectVersion.Default;
                project.Identity.Description = "Rhino commands created by Rhino AI.";
                if (project.Settings is RhinoCodePlatform.Projects.Rhino3DProjectSettings customSettings)
                {
                    customSettings.GenerateLayoutFile = false;
                }

                // Storage and pathing
                if (!RhinoCode.StorageSites.TryCreateStorage(projectFilePath, out IStorage storage))
                {
                    storage = RhinoCode.StorageSites.CreateStorage(projectFilePath);
                }

                // Write to disk
                project.Store(storage);
            }

            CachedProject = project;
        }

        project = CachedProject!;

        return project switch
        {
            null => ReturnResult.Failure("Could not create Project", "Report issue to developer"),
            _ => ReturnResult.Success(),
        };
    }

    // TODO : Is the Email too much?
    private static PublisherIdentity GetPublisherIdentity()
    {
        try
        {
            string[] result = RhinoApp.LoggedInUserName.Split(" - ");
            if (result.Length != 2)
                return PublisherIdentity.Empty;
            return new PublisherIdentity(result[1], result[0]);
        }
        catch { }

        return PublisherIdentity.Empty;
    }

    public ReturnResult AddCommandToProject(string commandName, string script)
    {
        try
        {
            ReturnResult result = TryGetProject(out IProject project);
            if (!result)
                return result;

            Uri scriptUri = new(Path.Combine(Paths.Directory, $"{commandName}.py"));

            SourceCode validate = new(LanguageSpec.Python3, script);

            if (!validate.TryCreateCode(out Code code))
                return ReturnResult.Failure("Could not create code from script");

            if (!code.TryBuild(new BuildContext(BuildKind.Run), out CompileException ex))
                return ReturnResult.Failure(ex.Message, "Fix compile issues in the script");

            File.WriteAllText(scriptUri.LocalPath, script);

            SourceCode source = new(LanguageSpec.Python3, commandName, script, scriptUri);

            project.Add(source);
            project.Store();

            Reload();

            return ReturnResult.Success();
        }
        catch (Exception anyEx)
        {
            return ReturnResult.Failure(anyEx.Message);
        }
    }

    public ReturnResult RemoveCommandFromProject(string commandName)
    {
        try
        {
            ReturnResult result = TryGetProject(out IProject project);
            if (!result)
                return result;

            foreach (ICode code in project.GetCodes())
            {
                if (!string.Equals(code.Title, commandName, StringComparison.OrdinalIgnoreCase))
                    continue;
                project.Remove(code.Id);
                project.Store();

                break;
            }

            Reload();
        }
        catch (Exception anyEx)
        {
            return ReturnResult.Failure(anyEx.Message);
        }

        return ReturnResult.Success();
    }

    public ReturnResult Build(bool reloadOnly)
    {
        ScriptingEnvironment.EnsurePythonRuntimeIsAvailable();
        try
        {
            ReturnResult result = TryGetProject(out IProject project);
            if (!result)
                return result;

            ProjectPackageBuild build = project.Settings.PackageBuild;

            if (!reloadOnly)
                project.Package(Host, build, Reporter);

            project.Preview(Host, build, Reporter);
        }
        catch (Exception anyEx)
        {
            return ReturnResult.Failure(anyEx.Message);
        }

        return ReturnResult.Success();
    }

    public static ReturnResult Reload()
    {
        ScriptingEnvironment.EnsurePythonRuntimeIsAvailable();

        ReturnResult result = TryCreate(out ScriptProjectRunner runner);
        if (!result)
            return result;

        result = runner.TryGetProject(out IProject project);
        if (!result)
            return result;

        ProjectPackageBuild build = project.Settings.PackageBuild;
        project.Package(runner.Host, build, runner.Reporter);
        project.Preview(runner.Host, build, runner.Reporter);

        return ReturnResult.Success();
    }

}
