using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI;

internal sealed class AISettingsDialog : Dialog
{
    private AISettingsPanel Panel { get; } = new();

    public AISettingsDialog()
    {
        Title = Rhino.UI.LOC.STR("AI Settings");
        Padding = new Padding(12);
        Size = new Size(720, 680);
        MinimumSize = new Size(560, 440);
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
