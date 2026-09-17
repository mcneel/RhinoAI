using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI;

internal sealed class AISettingsDialog : Dialog
{
    private AISettingsTabs Panel { get; }

    // Opened from one assistant, but it shows them all: the caller's own tab is simply the one in
    // front, so the title is the feature rather than that assistant.
    public AISettingsDialog(AIProfile profile, string? page = null)
    {
        Panel = new AISettingsTabs(profile, page);
        Title = Rhino.UI.LOC.STR("AI Settings");
        Padding = new Padding(12);
        Size = new Size(780, 740);
        MinimumSize = new Size(600, 460);
        Resizable = true;

        Button saveButton = new() { Text = Rhino.UI.LOC.STR("Save") };
        saveButton.Click += (_, _) =>
        {
            if (Panel.TryCommit(out _))
            {
                Close();
            }
        };

        Button closeButton = new() { Text = Rhino.UI.LOC.STR("Cancel") };
        closeButton.Click += (_, _) => Close();

        StackLayout buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { null, closeButton, saveButton },
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(Panel) { ScaleHeight = true },
                new TableRow(buttons),
            },
        };

        DefaultButton = saveButton;
        AbortButton = closeButton;
    }
}
