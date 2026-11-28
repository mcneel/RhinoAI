using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class AgentRemoval
{
    public static void Confirm(Control parent, AgentsPageViewModel page, AgentItemViewModel agent)
    {
        DialogResult answer = Eto.Forms.MessageBox.Show(
            parent,
            agent.RemoveToolTip,
            string.Format(LOC.STR("Remove {0}?"), agent.DisplayName),
            MessageBoxButtons.YesNo,
            MessageBoxType.Question,
            MessageBoxDefaultButton.No);

        if (answer == DialogResult.Yes)
            page.Remove(agent);
    }
}
