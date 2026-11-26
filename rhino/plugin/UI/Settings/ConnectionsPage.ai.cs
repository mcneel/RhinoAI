#if R9
using System.Linq.Expressions;

using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class ConnectionsPage : Panel
{
    private const int SectionGap = 10;
    private const int RowHeight = 30;
    private const int ActionWidth = 84;
    private const int FieldWidth = 220;
    private const int SnippetHeight = 176;
    private const int PromptHeight = 104;
    private const int ProblemIndent = 2;
    private const double CopiedSeconds = 1.5;

    public ConnectionsPage(ConnectionsPageViewModel model)
    {
        Model = model;
        DataContext = model;
        SetupBox = SetupSection();
        Scroller = new FittedScrollable(new StackLayout
        {
            Spacing = SectionGap,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { ServerSection(), AgentsSection(), SetupBox, AdvancedSection() },
        });
        Content = Scroller;
    }

    private ConnectionsPageViewModel Model { get; }

    private FittedScrollable Scroller { get; }

    private SectionBox SetupBox { get; }

    private SectionBox ServerSection()
    {
        StatusDot dot = new();
        dot.IsOnBinding.BindDataContext((ConnectionsPageViewModel m) => m.IsServerRunning, DualBindingMode.OneWay);

        Label status = new() { TextColor = Palette.Secondary };
        status.TextBinding.BindDataContext((ConnectionsPageViewModel m) => m.ServerStatus, DualBindingMode.OneWay);
        status.ToolTip = dot.ToolTip = LOC.STR("While it runs, apps work in this Rhino. When it is off, they start a separate Rhino (Advanced sets which version).");

        Label problem = new() { TextColor = Palette.Danger, Wrap = WrapMode.Word };
        problem.TextBinding.BindDataContext((ConnectionsPageViewModel m) => m.ServerProblemText, DualBindingMode.OneWay);
        problem.BindDataContext(control => control.Visible, (ConnectionsPageViewModel m) => m.HasServerProblem, DualBindingMode.OneWay);

        StackLayout description = new()
        {
            Spacing = 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Spacing = 6,
                    Items = { dot, new StackLayoutItem(status, true) },
                },
                problem,
            },
        };

        Button action = new() { Width = ActionWidth };
        action.TextBinding.BindDataContext((ConnectionsPageViewModel m) => m.ServerAction, DualBindingMode.OneWay);
        action.BindDataContext(control => control.ToolTip, (ConnectionsPageViewModel m) => m.ServerActionToolTip, DualBindingMode.OneWay);
        action.BindDataContext(control => control.Enabled, (ConnectionsPageViewModel m) => m.CanToggleServer, DualBindingMode.OneWay);
        action.Click += (_, _) => Model.ToggleServer();

        SectionBox box = new(LOC.STR("Rhino MCP server"), description, Centred(action), StartupRows());
        box.IsExpandedBinding.BindDataContext((ConnectionsPageViewModel m) => m.IsServerExpanded);
        return box;
    }

    private static Control StartupRows() => new StackLayout
    {
        Spacing = 6,
        Items =
        {
            StartupBox(
                LOC.STR("Start the MCP server when Rhino starts"),
                LOC.STR("Lets apps work in this Rhino without you clicking Start. Takes effect the next time Rhino starts."),
                m => m.AutoStart),
            StartupBox(
                LOC.STR("Load AI-created commands when Rhino starts"),
                LOC.STR("Brings back the commands agents made for you in earlier sessions. Takes effect the next time Rhino starts."),
                m => m.AutoLoadCommands),
        },
    };

    private static CheckBox StartupBox(string text, string toolTip, Expression<Func<ConnectionsPageViewModel, bool>> setting)
    {
        CheckBox box = new() { Text = text, ToolTip = toolTip };
        box.CheckedBinding
            .Convert(isChecked => isChecked ?? false, isOn => (bool?)isOn)
            .BindDataContext(setting);
        return box;
    }

    private SectionBox AgentsSection()
    {
        StackLayout rows = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (ExternalAgentItemViewModel agent in Model.Agents)
        {
            if (rows.Items.Count > 0)
                rows.Items.Add(SectionBox.Separator());
            rows.Items.Add(AgentRow(agent));
        }

        SectionBox box = new(LOC.STR("External agents"), null, null, rows);
        box.IsExpandedBinding.BindDataContext((ConnectionsPageViewModel m) => m.IsAgentsExpanded);
        return box;
    }

    private Control AgentRow(ExternalAgentItemViewModel agent)
    {
        RowTextView text = new() { Height = RowHeight };
        text.TextBinding.BindDataContext((ExternalAgentItemViewModel m) => m.Text, DualBindingMode.OneWay);
        text.BindDataContext(control => control.ToolTip, (ExternalAgentItemViewModel m) => m.ToolTip, DualBindingMode.OneWay);

        Button action = new() { Width = ActionWidth };
        action.TextBinding.BindDataContext((ExternalAgentItemViewModel m) => m.ActionText, DualBindingMode.OneWay);
        action.BindDataContext(control => control.ToolTip, (ExternalAgentItemViewModel m) => m.ActionToolTip, DualBindingMode.OneWay);
        action.BindDataContext(control => control.Visible, (ExternalAgentItemViewModel m) => m.HasAction, DualBindingMode.OneWay);
        action.Click += (_, _) => Model.ToggleAgent(agent);

        Label problem = new() { TextColor = Palette.Danger, Wrap = WrapMode.Word };
        problem.TextBinding.BindDataContext((ExternalAgentItemViewModel m) => m.ProblemText, DualBindingMode.OneWay);

        LinkButton fix = new();
        fix.TextBinding.BindDataContext((ExternalAgentItemViewModel m) => m.ProblemAction, DualBindingMode.OneWay);
        fix.Click += (_, _) => Fix(agent);

        TableLayout problemRow = new()
        {
            Padding = new Padding(ProblemIndent, 0, 0, 6),
            Spacing = new Size(0, 2),
            Rows = { new TableRow(problem), new TableRow(new TableLayout(new TableRow(fix, null))) },
        };
        problemRow.BindDataContext(control => control.Visible, (ExternalAgentItemViewModel m) => m.HasProblem, DualBindingMode.OneWay);

        ContextMenu menu = AgentMenu(agent);
        text.ContextMenu = menu;
        return new TableLayout
        {
            DataContext = agent,
            ContextMenu = menu,
            Rows =
            {
                new TableRow(new TableLayout
                {
                    Spacing = new Size(12, 0),
                    Rows = { new TableRow(new TableCell(text, true), Centred(action)) },
                }),
                new TableRow(problemRow),
            },
        };
    }

    private ContextMenu AgentMenu(ExternalAgentItemViewModel agent)
    {
        ButtonMenuItem change = new();
        change.Click += (_, _) => Model.ToggleAgent(agent);
        SeparatorMenuItem separator = new();
        ButtonMenuItem copy = new() { Text = LOC.STR("Copy Config Path") };
        copy.Click += (_, _) => Clipboard.Instance.Text = agent.ConfigPath;

        ContextMenu menu = new() { Items = { change, separator, copy } };
        menu.Opening += (_, _) =>
        {
            change.Text = agent.ActionMenuText;
            change.Visible = agent.HasAction;
            separator.Visible = agent.HasAction;
        };
        return menu;
    }

    private void Fix(ExternalAgentItemViewModel agent)
    {
        switch (agent.Problem?.Change)
        {
            case AgentChange.Add:
                Model.IsSetupExpanded = true;
                Scroller.Reveal(SetupBox, SectionGap);
                break;

            case AgentChange.Remove:
                Clipboard.Instance.Text = agent.ConfigPath;
                break;

            case null:
                break;

            default:
                throw new InvalidOperationException("Unknown agent change.");
        }
    }

    private SectionBox SetupSection()
    {
        TextArea snippet = Code(SnippetHeight);
        snippet.TextBinding.BindDataContext((ConnectionsPageViewModel m) => m.Snippet, DualBindingMode.OneWay);

        TextArea prompt = Code(PromptHeight);
        prompt.TextBinding.BindDataContext((ConnectionsPageViewModel m) => m.Prompt, DualBindingMode.OneWay);

        TableLayout body = new()
        {
            Spacing = new Size(0, 6),
            Rows =
            {
                new TableRow(CopyHeader(LOC.STR("Add this entry to the app's MCP settings."), snippet)),
                new TableRow(snippet),
                new TableRow(new Panel
                {
                    Padding = new Padding(0, 8, 0, 0),
                    Content = CopyHeader(LOC.STR("Or paste this prompt into the app, and it adds the entry itself."), prompt),
                }),
                new TableRow(prompt),
            },
        };

        SectionBox box = new(LOC.STR("Set up another app"), null, null, body);
        box.IsExpandedBinding.BindDataContext((ConnectionsPageViewModel m) => m.IsSetupExpanded);
        return box;
    }

    private SectionBox AdvancedSection()
    {
        TableLayout fields = new() { Spacing = new Size(12, 10) };
        foreach (RouterOptionItemViewModel option in Model.Options)
            fields.Rows.Add(OptionRow(option));

        TableLayout body = new()
        {
            Spacing = new Size(0, 10),
            Rows =
            {
                new TableRow(Note(LOC.STR("They go into the snippet and into apps you add Rhino to here, and are not saved. To update an app that already has Rhino, click Remove, then Add."))),
                new TableRow(fields),
            },
        };

        SectionBox box = new(LOC.STR("Advanced"), null, null, body);
        box.IsExpandedBinding.BindDataContext((ConnectionsPageViewModel m) => m.IsAdvancedExpanded);
        return box;
    }

    private static TableRow OptionRow(RouterOptionItemViewModel option)
    {
        TextBox field = new()
        {
            DataContext = option,
            PlaceholderText = option.Placeholder,
            ToolTip = option.Help,
            Width = FieldWidth,
        };
        field.TextBinding.BindDataContext((RouterOptionItemViewModel m) => m.Value);

        StackLayout text = new()
        {
            Spacing = 1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new Label { Text = option.Label, Font = SystemFonts.Bold() }, Note(option.Help) },
        };
        return new TableRow(new TableCell(text, true), Centred(field));
    }

    private static Control CopyHeader(string text, TextArea source)
    {
        string label = LOC.STR("Copy");
        Button copy = new() { Text = label, Width = ActionWidth };
        UITimer reset = new() { Interval = CopiedSeconds };
        reset.Elapsed += (_, _) =>
        {
            reset.Stop();
            copy.Text = label;
        };
        copy.Click += (_, _) =>
        {
            Clipboard.Instance.Text = source.Text;
            copy.Text = LOC.STR("Copied");
            reset.Start();
        };
        copy.UnLoad += (_, _) => reset.Stop();

        return new TableLayout
        {
            Spacing = new Size(12, 0),
            Rows = { new TableRow(new TableCell(Note(text), true), Centred(copy)) },
        };
    }

    private static TextArea Code(int height) => new()
    {
        ReadOnly = true,
        Wrap = true,
        Height = height,
        Font = Eto.Drawing.Fonts.Monospace(SystemFonts.Default().Size - 1),
    };

    private static Label Note() => new() { TextColor = Palette.Secondary, Wrap = WrapMode.Word };

    private static Label Note(string text)
    {
        Label note = Note();
        note.Text = text;
        return note;
    }

    private static Control Centred(Control control) => new StackLayout
    {
        Orientation = Orientation.Horizontal,
        VerticalContentAlignment = VerticalAlignment.Center,
        Items = { control },
    };
}
#endif
