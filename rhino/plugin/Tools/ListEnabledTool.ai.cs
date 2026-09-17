namespace Rhino.AI.Tools;

// What this caller is allowed to do, right now.
//
// tools/list already hides what is switched off, but it is answered once when the session starts and
// the user can change their mind at any point afterwards. This is the same question asked live, and
// it also says which calls will stop to ask — which tools/list has no way to express.
[McpServerToolType]
internal static class ListEnabledTool
{
    [McpServerTool("list_enabled", "List Enabled Functionality", true, false)]
    [Description("List the tools this caller may currently use, each with how it runs: \"on\" runs "
        + "straight away, \"ask\" stops and asks the user to allow that call before it happens. Also "
        + "lists what is switched off, so it is not attempted. Read this when a call was refused, or "
        + "before planning a long piece of work, rather than assuming the list you were given at the "
        + "start of the session still holds: the user can change any of it while you are running, in "
        + "Rhino's AI settings.")]
    public static IToolResult ListEnabled()
    {
        AIProfile? asking = ToolAudience.Profile;

        List<object> enabled = [];
        List<string> disabled = [];

        foreach (ToolInfo tool in ToolCatalog.All.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            // The external route is not gated: everything it can see, it may call.
            ToolMode mode = asking is AIProfile profile ? ToolPolicy.Mode(profile, tool) : ToolMode.On;

            if (mode == ToolMode.Off)
                disabled.Add(tool.Name);
            else
                enabled.Add(new { name = tool.Name, title = tool.Title, mode = ToolModes.Format(mode) });
        }

        return Success(new
        {
            assistant = asking is AIProfile who ? AIProfiles.Wire(who) : "external",
            enabled,
            disabled,
        });
    }
}
