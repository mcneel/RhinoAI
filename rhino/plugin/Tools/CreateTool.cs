#if RHINOCODE
using RhinoAI.ScriptProjects;

namespace RhinoAI.Tools;

[McpServerToolType]
public static class CreateTool
{
    [McpServerTool("manage_plugin_commands", "Manage Plugin Commands", false, true)]
    [Description("Give the user a reusable Rhino command. After every call the new or changed command is usable immediately. Use this when the user wants to create a reusable tool ('create me a command', 'make me a tool for this'). Rhino 9 or later only. Returns JSON with a null error on success.")]
    public static string ManagePluginCommands(
        RhinoDoc doc,
        [Description("add | update | delete")] string action,
        [Description("Command name the user will type in Rhino, Letters, digits and underscores only, no spaces.")] string commandName,
        [Description("Python 3 source for the command. Required for add and update. Use `__rhino_doc__` as the document handle, as with run_python.")] string? script = null,
        [Description("Optional Icon if adding or updating a command (recommended)")] string? svg = null)
    {

        if (!ScriptProjectRunner.IsSupportedRhino)
        {
            return ReturnResult.Failure($"This needs Rhino 9 or later; this is Rhino {RhinoApp.Version.Major}.");
        }

        if (!PluginNaming.TryParseAction(action, out PluginCommandAction parsedAction))
        {
            return ReturnResult.Failure($"Unknown action \"{action}\"", "Use add, update or delete.");
        }

        string originalName = commandName;
        CommandNameProblem problem = PluginNaming.TryCoerceCommandName(ref commandName);
        if (problem is not CommandNameProblem.None)
        {
            return ReturnResult.Failure("Command Name has issues", PluginNaming.Describe(problem, originalName));
        }

        ReturnResult result = ScriptProjectRunner.TryCreate(out IProjectRunner runner);
        if (!result)
            return result;

        if (parsedAction is PluginCommandAction.Delete)
        {
            ReturnResult removeResult = runner.RemoveCommandFromProject(commandName);
            if (!removeResult) return removeResult;
            
            return ReturnResult.Success($"Comand {commandName} was removed successfully");
        }
        else if (parsedAction is PluginCommandAction.Add or PluginCommandAction.Update)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return ReturnResult.Failure($"A Python script is required to {action.ToLowerInvariant()} a command.");
            }
            ReturnResult addResult = runner.AddCommandToProject(commandName, script, svg);
            if (!addResult)
                return addResult;

            return ReturnResult.Success($"Comand {commandName} is now loaded and ready");
        }
        
        return ReturnResult.Failure($"No result for given action and inputs");
    }

}
#endif
