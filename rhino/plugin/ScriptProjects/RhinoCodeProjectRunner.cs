#if R9

using System.IO;

using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Storage;
using Rhino.Runtime.Code.Projects;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;
using Rhino.Runtime.Code.Diagnostics;

namespace RhinoAI.ScriptProjects;

internal class RhinoCodeProjectRunner : IProjectRunner
{

    private IProject? CachedProject { get; set; }

    public ScriptProjectPaths Paths { get; }

    private IProgress<ProgressReport> Reporter { get; } = new SilentProgressReporter();

    public RhinoCodeProjectRunner()
    {
        Paths = ScriptProjectPaths.For(null);
        Paths.Directory.EnsureDirectory();
    }

    private ReturnResult TryGetProject(out IProject project)
    {
        project = default!;

        if (CachedProject is null)
        {
            Uri projectFilePath = new(Paths.ProjectFile);

            Paths.Directory.EnsureDirectory();

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

    public ReturnResult AddCommandToProject(string commandName, string script, string? svg)
    {
        try
        {
            ReturnResult result = TryGetProject(out IProject project);
            if (!result)
                return result;

            Uri scriptUri = new(Path.Combine(Paths.Directory, $"{commandName}.py"));

            EnsurePython3Header(ref script);

            SourceCode validate = new(LanguageSpec.Python3, script);

            if (!validate.TryCreateCode(out Code code))
                return ReturnResult.Failure("Could not create code from script");

            if (!code.TryBuild(new BuildContext(BuildKind.Run), out CompileException ex))
            {
                string message = ex.Message;
                string guidance = "";
                
                foreach(Diagnostic? diagnostic in ex.Diagnosis)
                {
                    if (diagnostic is null) continue;
                    guidance += $"DAIG : {diagnostic.Message} @ Line:{diagnostic.Reference.Position.LineNumber}, Col:{diagnostic.Reference.Position.ColumnNumber}.";
                }

                if (string.IsNullOrEmpty(guidance))
                {
                    guidance = $"STACK TRACE : {ex.StackTrace}";
                }

                return ReturnResult.Failure(message, guidance);
            }

            File.WriteAllText(scriptUri.LocalPath, script);

            SourceCode source = new(LanguageSpec.Python3, commandName, script, scriptUri);

            // Remove before update
            RemoveCommandFromProject(commandName);

            ProjectCode projectCode = project.Add(source);
            RhinoCodeProjects.SetIcon(projectCode, svg);

            if (!project.TryStore()) return ReturnResult.Failure($"Could not save script {scriptUri.LocalPath}");

            Reload();

            return ReturnResult.Success();
        }
        catch (Exception anyEx)
        {
            return ReturnResult.Failure(anyEx.Message);
        }
    }

    private static void EnsurePython3Header(ref string script)
    {
        const string HEADER = "#! python 3\n";
        if (script.Trim().StartsWith(HEADER)) return;

        script = script.Insert(0, HEADER);
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

            dynamic? host = ScriptingEnvironment.Host;
            if (host is null)
                return ReturnResult.Failure("Could not resolve the script host");

            ProjectPackageBuild build = project.Settings.PackageBuild;

            if (!reloadOnly)
                project.Package(host, build, Reporter);

            project.Preview(host, build, Reporter);
        }
        catch (Exception anyEx)
        {
            return ReturnResult.Failure(anyEx.Message);
        }

        return ReturnResult.Success();
    }

    public ReturnResult Reload() => Build(false);

}

#endif
