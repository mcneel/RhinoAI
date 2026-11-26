using Eto.Drawing;
using Eto.Forms;

namespace Rhino.AI.UI.Settings;

internal sealed class SectionBox : Panel
{
    private const int HeaderSpacing = 8;
    private const int BodyGap = 8;

    public SectionBox(string title, Control? description, Control? action, Control body)
    {
        Disclosure = new Disclosure();

        Label heading = new() { Text = title, Font = SystemFonts.Bold(), VerticalAlignment = VerticalAlignment.Center };
        Control text = description is null
            ? heading
            : new StackLayout
            {
                Spacing = 2,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items = { heading, description },
            };
        IEnumerable<Control> clickable = text is Eto.Forms.Container container ? container.Children.Prepend(text) : [text];
        foreach (Control control in clickable)
            control.MouseDown += ToggleFromHeader;

        TableRow header = new(new TableCell(Disclosure), new TableCell(text, true));
        if (action is not null)
            header.Cells.Add(new TableCell(action));

        Body = new TableLayout
        {
            Padding = new Padding(0, BodyGap, 0, 0),
            Spacing = new Size(0, BodyGap),
            Rows = { new TableRow(Separator()), new TableRow(body) },
            Visible = false,
        };
        Disclosure.ExpandedChanged += (_, _) => Body.Visible = Disclosure.IsExpanded;

        Padding = new Padding(8);
        Content = new TableLayout
        {
            Rows =
            {
                new TableRow(new TableLayout { Spacing = new Size(HeaderSpacing, 0), Rows = { header } }),
                new TableRow(Body),
            },
        };
    }

    public BindableBinding<SectionBox, bool> IsExpandedBinding
        => new(this, box => box.Disclosure.IsExpanded, (box, value) => box.Disclosure.IsExpanded = value, (box, handler) => box.Disclosure.ExpandedChanged += handler, (box, handler) => box.Disclosure.ExpandedChanged -= handler);

    private Disclosure Disclosure { get; }

    private TableLayout Body { get; }

    public static Control Separator() => new Panel { Height = 1, BackgroundColor = Palette.Rule };

    private void ToggleFromHeader(object? sender, MouseEventArgs e)
    {
        if (e.Buttons != MouseButtons.Primary)
            return;

        Disclosure.Toggle();
        e.Handled = true;
    }
}
