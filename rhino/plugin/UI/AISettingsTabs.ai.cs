using Eto.Forms;

namespace Rhino.AI;

// Every assistant's settings in one place, on one row of tabs: the agents and the MCP servers they
// all share, then one tab per assistant for what that assistant is allowed to do.
//
// Settings used to be opened per panel, which meant the Grasshopper assistant's permissions could
// only be reached from the Grasshopper assistant — including when the thing you wanted to change was
// why it had just asked you something.
internal sealed class AISettingsTabs : Panel
{
    private List<AISettingsPanel> Pages { get; } = [];
    private TabControl Tabs { get; } = new();

    // The tab index of each assistant's permissions, by profile.
    private Dictionary<AIProfile, int> Assistants { get; } = [];

    private const int McpTab = 1;

    /// <summary>What a caller asks for when "open permissions" has to open them.</summary>
    public const string PermissionsPage = "Permissions";

    public AISettingsTabs(AIProfile profile, string? page = null)
    {
        // The agents and the servers are the Rhino assistant's pages because its settings keys are
        // the ones that predate profiles, and the MCP servers are shared by every agent anyway.
        AISettingsPanel main = new(AIProfile.Rhino);
        Pages.Add(main);
        Tabs.Pages.Add(new TabPage { Text = Rhino.UI.LOC.STR("AI Agents"), Content = main.Agents() });
        Tabs.Pages.Add(new TabPage { Text = Rhino.UI.LOC.STR("MCP Servers"), Content = main.Mcp() });

        Assistants[AIProfile.Rhino] = Tabs.Pages.Count;
        Tabs.Pages.Add(new TabPage { Text = AIProfiles.Name(AIProfile.Rhino), Content = main.Permissions() });

        foreach (AIProfile each in AIProfiles.All)
        {
            if (each == AIProfile.Rhino)
                continue;

            // The GrasshopperAssistant command is parked in _discard, so there is no way to open that
            // assistant at all and a tab of permissions for it is noise. Delete these two lines when
            // the command comes back.
            if (each == AIProfile.Grasshopper)
                continue;

            AISettingsPanel panel = new(each);
            Pages.Add(panel);
            Assistants[each] = Tabs.Pages.Count;
            Tabs.Pages.Add(new TabPage { Text = AIProfiles.Name(each), Content = panel.Permissions() });
        }

        Content = Tabs;
        Show(profile, page);
    }

    /// <summary>Lands on an assistant's own tab when the caller asked for its permissions, and on the
    /// agents otherwise, which is what someone opening settings from a panel is usually after.</summary>
    public void Show(AIProfile profile, string? page)
    {
        Tabs.SelectedIndex = string.Equals(page, PermissionsPage, StringComparison.OrdinalIgnoreCase)
            && Assistants.TryGetValue(profile, out int assistant)
                ? assistant
                : 0;
    }

    /// <summary>Saves every assistant, since the dialog shows every assistant. The first page that
    /// refuses stops the rest, so nothing is half-written and the page that objected is on screen.</summary>
    public bool TryCommit(out string error)
    {
        foreach (AISettingsPanel panel in Pages)
        {
            if (!panel.TryCommit(out string refused))
            {
                // The only page that refuses is the MCP JSON, so that is the one to show.
                Tabs.SelectedIndex = McpTab;
                error = refused;
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
