using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

// Standalone modal host for the shared AISettingsPanel, opened from the AIPanel gear button and the
// AISettings command. The same panel is also hosted by AIOptionsPage in the Rhino Options dialog;
// all commit/validation logic lives in the panel so the two hosts can't drift.
internal sealed class AISettingsDialog : Dialog
{
    private AISettingsPanel Panel { get; } = new();

    public AISettingsDialog()
    {
        Title = "AI Settings";
        Padding = new Padding(12);
        Size = new Size(720, 680);
        MinimumSize = new Size(560, 440);
        Resizable = true;

        Button saveButton = new() { Text = "Save" };
        saveButton.Click += (_, _) =>
        {
            if (Panel.TryCommit(out _))
                Close();
        };

        Button closeButton = new() { Text = "Cancel" };
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
