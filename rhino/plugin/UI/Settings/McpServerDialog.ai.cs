#if R9
using System.Linq.Expressions;

using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class McpServerDialog : Dialog<IReadOnlyList<IMcpDefinition>?>
{
    private const int LabelWidth = 90;
    private const int AreaHeight = 58;
    private const int GroupGap = 6;

    public McpServerDialog(McpServerDialogViewModel model)
    {
        DataContext = model;
        Title = model.Title;
        Resizable = true;
        Padding = new Padding(8);
        MinimumSize = new Size(460, 420);
        Size = new Size(560, 500);
        this.UseRhinoStyle();

        Button confirm = new() { Text = model.ConfirmText };
        Button cancel = new() { Text = LOC.STR("Cancel") };
        confirm.Click += (_, _) =>
        {
            if (model.Confirm() is IReadOnlyList<IMcpDefinition> servers)
                Close(servers);
        };
        cancel.Click += (_, _) => Close(null);
        PositiveButtons.Add(confirm);
        NegativeButtons.Add(cancel);
        DefaultButton = confirm;
        AbortButton = cancel;

        TabControl tabs = new()
        {
            Pages =
            {
                new TabPage { Text = LOC.STR("Form"), Content = FormPage() },
                new TabPage { Text = LOC.STR("JSON"), Content = JsonPage() },
            },
        };
        tabs.SelectedIndexBinding.BindDataContext((McpServerDialogViewModel m) => m.SelectedTab);

        Label error = new() { TextColor = Palette.Danger, Wrap = WrapMode.Word };
        error.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.ErrorText, DualBindingMode.OneWay);
        error.BindDataContext(label => label.Visible, (McpServerDialogViewModel m) => m.HasError, DualBindingMode.OneWay);

        Content = new TableLayout
        {
            Spacing = new Size(5, 5),
            Rows =
            {
                new TableRow(tabs) { ScaleHeight = true },
                new TableRow(error),
            },
        };
    }

    private static Control FormPage()
    {
        TextBox name = new();
        name.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Name);

        RadioButtonList type = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = new Size(12, 0),
            Items = { LOC.STR("Command"), LOC.STR("URL") },
        };
        type.SelectedIndexBinding.BindDataContext((McpServerDialogViewModel m) => m.TypeIndex);

        TextBox command = new() { PlaceholderText = LOC.STR("For example, npx") };
        command.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Command);

        TextArea arguments = Area();
        arguments.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Arguments);

        TextArea environment = Area();
        environment.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Environment);

        TextBox url = new() { PlaceholderText = LOC.STR("For example, https://example.com/mcp") };
        url.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Url);

        TextArea headers = Area();
        headers.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Headers);

        foreach (Control input in new Control[] { name, type, command, arguments, environment, url, headers })
            input.BindDataContext(control => control.Enabled, (McpServerDialogViewModel m) => m.IsFormEnabled, DualBindingMode.OneWay);

        Label formNote = Note();
        formNote.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.FormNote, DualBindingMode.OneWay);
        Panel formNoteRow = new() { Padding = new Padding(0, 0, 0, GroupGap), Content = formNote };
        formNoteRow.BindDataContext(panel => panel.Visible, (McpServerDialogViewModel m) => m.HasFormNote, DualBindingMode.OneWay);

        Label extraNote = Note();
        extraNote.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.ExtraNote, DualBindingMode.OneWay);

        return new TableLayout
        {
            Padding = new Padding(8),
            Rows =
            {
                new TableRow(formNoteRow),
                new TableRow(Group(0, null, Row(LOC.STR("Name"), name), Row(LOC.STR("Type"), type))),
                new TableRow(Group(GroupGap, m => m.IsCommand,
                    Row(LOC.STR("Command"), command),
                    Row(LOC.STR("Arguments"), Hinted(arguments, LOC.STR("One per line"))),
                    Row(LOC.STR("Environment"), Hinted(environment, LOC.STR("KEY=value, one per line"))))),
                new TableRow(Group(GroupGap, m => m.IsUrl,
                    Row(LOC.STR("URL"), url),
                    Row(LOC.STR("Headers"), Hinted(headers, LOC.STR("Name: value, one per line"))))),
                new TableRow(Group(GroupGap * 2, m => m.HasExtraNote, new TableRow(new Panel { Width = LabelWidth }, new TableCell(extraNote, true)))),
                new TableRow { ScaleHeight = true },
            },
        };
    }

    private static Control JsonPage()
    {
        TextArea json = new()
        {
            Font = Eto.Drawing.Fonts.Monospace(SystemFonts.Default().Size),
            Wrap = false,
            AcceptsTab = true,
            SpellCheck = false,
        };
        json.TextBinding.BindDataContext((McpServerDialogViewModel m) => m.Json);

        Label help = new()
        {
            Text = LOC.STR("Paste one or more servers by name, or a whole \"mcpServers\" block from another app's settings."),
            TextColor = Palette.Secondary,
            Wrap = WrapMode.Word,
        };

        return new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(5, 6),
            Rows =
            {
                new TableRow(help),
                new TableRow(json) { ScaleHeight = true },
            },
        };
    }

    private static TableLayout Group(int top, Expression<Func<McpServerDialogViewModel, bool>>? visible, params TableRow[] rows)
    {
        TableLayout group = new() { Padding = new Padding(0, top, 0, 0), Spacing = new Size(8, GroupGap) };
        foreach (TableRow row in rows)
            group.Rows.Add(row);

        if (visible is not null)
            group.BindDataContext(control => control.Visible, visible, DualBindingMode.OneWay);
        return group;
    }

    private static TableRow Row(string text, Control field)
    {
        Label label = new()
        {
            Text = text,
            Width = LabelWidth,
            VerticalAlignment = field is StackLayout ? VerticalAlignment.Top : VerticalAlignment.Center,
        };

        return new TableRow(label, new TableCell(field, true));
    }

    private static StackLayout Hinted(Control field, string hint) => new()
    {
        Spacing = 2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            field,
            new Label { Text = hint, TextColor = Palette.Secondary, Font = SystemFonts.Default(SystemFonts.Default().Size - 1) },
        },
    };

    private static TextArea Area() => new()
    {
        Height = AreaHeight,
        AcceptsTab = false,
        AcceptsReturn = true,
        Wrap = false,
        SpellCheck = false,
    };

    private static Label Note() => new()
    {
        TextColor = Palette.Secondary,
        Wrap = WrapMode.Word,
    };
}
#endif
