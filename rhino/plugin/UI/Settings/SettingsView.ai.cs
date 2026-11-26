#if R9
using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class SettingsView : Panel
{
    private const int PagePadding = 8;

    public SettingsView(SettingsViewModel model)
    {
        Model = model;
        DataContext = model;
        AgentsTab = Page(LOC.STR("Agents"), new AgentsPage(model.Agents));
        Tabs = new TabControl
        {
            Pages =
            {
                AgentsTab,
                Page(LOC.STR("Permissions"), new PermissionsPage(model.Permissions)),
                Page(LOC.STR("Connections"), new ConnectionsPage(model.Connections)),
                Page(LOC.STR("Usage"), new UsagePage(model.Usage)),
            },
        };
        Tabs.SelectedPage = AgentsTab;
        Content = Tabs;
    }

    private SettingsViewModel Model { get; }

    private TabControl Tabs { get; }

    private TabPage AgentsTab { get; }

    public bool TryCommit()
    {
        if (Model.TryCommit())
            return true;

        Tabs.SelectedPage = AgentsTab;
        return false;
    }

    private static TabPage Page(string title, Control content) => new()
    {
        Text = title,
        Padding = new Padding(PagePadding),
        Content = content,
    };
}
#endif
