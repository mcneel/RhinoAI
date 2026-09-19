#if R9
using Rhino.AI.ScriptProjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class CreateTool
{
    [McpServerTool("manage_plugin_commands", "Manage Plugin Commands", false, true)]
    [Description("Give the user a reusable Rhino command. After every call the new or changed command is usable immediately. Use this when the user wants to create a reusable tool ('create me a command', 'make me a tool for this'). Rhino 9 or later only.")]
    public static IToolResult ManagePluginCommands(
        RhinoDoc doc,
        [Description("add | update | delete")] string action,
        [Description("Command name the user will type in Rhino, Letters, digits and underscores only, no spaces.")] string commandName,
        [Description("C# source for the command. Required for add and update. Use `__rhino_doc__` as the document handle, as with run_csharp.")] string? script = null,
        [Description("Optional Icon if adding or updating a command (recommended)")] string? svg = null)
    {
        if (!ScriptProjectRunner.IsSupportedRhino)
            return Failure(ToolError.Unsupported, $"This needs Rhino 9 or later; this is Rhino {RhinoApp.Version.Major}.");

        if (!PluginNaming.TryParseAction(action, out PluginCommandAction parsedAction))
            return Failure(ToolError.BadArgument, $"Unknown action \"{action}\"", "Use add, update or delete.");

        string originalName = commandName;
        CommandNameProblem problem = PluginNaming.TryCoerceCommandName(ref commandName);
        if (problem is not CommandNameProblem.None)
            return Failure(ToolError.BadArgument, "Command Name has issues", PluginNaming.Describe(problem, originalName));

        Coercions coerced = new();
        if (!string.Equals(commandName, originalName, StringComparison.Ordinal))
            coerced.Note($"'{originalName}' is not a legal command name, so it was created as '{commandName}'");

        IToolResult result = ScriptProjectRunner.TryCreate(out IProjectRunner runner);
        if (result.Error is not null)
            return result;

        if (parsedAction is PluginCommandAction.Delete)
        {
            IToolResult removeResult = runner.RemoveCommandFromProject(commandName);
            if (removeResult.Error is not null)
                return removeResult;

            return Success(ContentBlock.CreateText($"Command {commandName} was removed successfully"), coerced.Guidance);
        }

        if (parsedAction is PluginCommandAction.Add or PluginCommandAction.Update)
        {
            if (string.IsNullOrWhiteSpace(script))
                return Failure(ToolError.BadArgument, $"A C# script is required to {action.ToLowerInvariant()} a command.");

            IToolResult addResult = runner.AddCommandToProject(commandName, script, svg);
            if (addResult.Error is not null)
                return addResult;

            return Success(ContentBlock.CreateText($"Command {commandName} is now loaded and ready"), coerced.Guidance);
        }

        return Failure(ToolError.Failed, "No result for given action and inputs");
    }

}
#endif
