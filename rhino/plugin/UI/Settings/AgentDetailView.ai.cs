#if R9
using System.Threading;

using Eto.Drawing;
using Eto.Forms;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class AgentDetailView : Panel
{
    private const int SectionGap = 10;
    private const int FocusRingRoom = 4;
    private const string SavedKeyMask = "000000000000";
    private const int DropDownWidth = 160;
    private const int PromptHeight = 110;
    private const float NameGrowth = 3f;

    public AgentDetailView(AgentsPageViewModel page, AgentItemViewModel agent)
    {
        Page = page;
        Agent = agent;
        DataContext = agent;
        Content = new FittedScrollable(new StackLayout
        {
            Padding = new Padding(FocusRingRoom),
            Spacing = SectionGap,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { Header(), Problems(), ModelSection(), ConnectionSection(), PromptSection(), SectionBox.Separator(), RemoveRow() },
        });
    }

    private AgentsPageViewModel Page { get; }

    private AgentItemViewModel Agent { get; }

    private TextBox NameBox { get; } = new()
    {
        Font = SystemFonts.Bold(SystemFonts.Default().Size + NameGrowth),
        PlaceholderText = LOC.STR("Untitled agent"),
        ToolTip = LOC.STR("Agent name"),
    };

    public void FocusName()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private Control Header()
    {
        TextBox name = NameBox;
        name.TextBinding.BindDataContext((AgentItemViewModel m) => m.Name);

        CheckBox enabled = new()
        {
            Text = LOC.STR("Enabled"),
            ToolTip = LOC.STR("Turn off to stop using this agent without removing it."),
        };
        enabled.CheckedBinding
            .Convert(isChecked => isChecked ?? false, isOn => (bool?)isOn)
            .BindDataContext((AgentItemViewModel m) => m.Enabled);

        StackLayout text = new()
        {
            Spacing = 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { name },
        };
        if (Agent.Subtitle.Length > 0)
            text.Items.Add(new Label { Text = Agent.Subtitle, TextColor = Palette.Secondary });

        return new TableLayout
        {
            Spacing = new Size(16, 0),
            Rows = { new TableRow(new TableCell(text, true), new TableCell(Centred(enabled))) },
        };
    }

    private static Control Problems()
    {
        Label problems = new() { TextColor = Palette.Danger, Wrap = WrapMode.Word };
        problems.TextBinding.BindDataContext((AgentItemViewModel m) => m.ProblemText, DualBindingMode.OneWay);
        problems.BindDataContext(control => control.Visible, (AgentItemViewModel m) => m.HasProblems, DualBindingMode.OneWay);
        return problems;
    }

    private SectionBox ModelSection()
    {
        DropDown choices = new() { Width = DropDownWidth, ToolTip = LOC.STR("New conversations start with this model.") };
        choices.ItemTextBinding = Binding.Property((ModelItemViewModel m) => m.Label);
        bool isFilling = false;
        void Fill()
        {
            isFilling = true;
            choices.DataStore = Agent.NamedModels;
            choices.SelectedValue = Agent.EffectiveModel;
            choices.Enabled = Agent.HasNamedModels;
            isFilling = false;
        }

        Fill();
        choices.SelectedValueChanged += (_, _) =>
        {
            if (!isFilling && choices.SelectedValue is ModelItemViewModel chosen)
                Agent.EffectiveModel = chosen;
        };
        Agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AgentItemViewModel.NamedModels) or nameof(AgentItemViewModel.EffectiveModel))
                Fill();
        };

        SectionBox box = new(LOC.STR("Models"), null, Centred(choices), ModelsEditor());
        box.IsExpandedBinding.Bind(Page, (AgentsPageViewModel m) => m.IsModelExpanded);
        return box;
    }

    private Control ModelsEditor()
    {
        Panel rows = new();
        void Rebuild() => rows.Content = ModelRows();
        Rebuild();
        Agent.Models.CollectionChanged += (_, _) => Rebuild();

        Button add = new() { Text = LOC.STR("Add Model") };
        add.Click += (_, _) =>
        {
            Agent.AddModel();
            Application.Instance.AsyncInvoke(() => LastTextBox(rows)?.Focus());
        };

        StackLayout buttons = new() { Orientation = Orientation.Horizontal, Spacing = 6, Items = { add } };
        if (Agent.IsLocal)
            buttons.Items.Add(LoadModelsButton());

        Label note = new() { Wrap = WrapMode.Word };
        note.TextBinding.BindDataContext((AgentItemViewModel m) => m.ModelsNoteText, DualBindingMode.OneWay);
        note.BindDataContext(control => control.Visible, (AgentItemViewModel m) => m.HasModelsNote, DualBindingMode.OneWay);
        void Colour() => note.TextColor = Agent.IsModelsNoteError ? Palette.Danger : Palette.Secondary;
        Colour();
        Agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentItemViewModel.IsModelsNoteError))
                Colour();
        };

        return new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows = { new TableRow(rows), new TableRow(buttons), new TableRow(note) },
        };
    }

    private Control ModelRows()
    {
        if (Agent.Models.Count == 0)
            return Note(Agent.NoModelsHelp);

        TableLayout table = new() { Spacing = new Size(6, 6) };
        table.Rows.Add(new TableRow(new TableCell(Heading(LOC.STR("Name")), true), new TableCell(Heading(LOC.STR("ID")), true), new TableCell(null, false)));
        foreach (ModelItemViewModel model in Agent.Models)
        {
            TextBox name = new() { DataContext = model, PlaceholderText = LOC.STR("Model name") };
            name.TextBinding.BindDataContext((ModelItemViewModel m) => m.Name);
            TextBox id = new() { DataContext = model, PlaceholderText = LOC.STR("Model ID"), Font = Mono() };
            id.TextBinding.BindDataContext((ModelItemViewModel m) => m.Id);
            RemoveGlyphButton remove = new() { ToolTip = LOC.STR("Remove model") };
            remove.Click += (_, _) => Agent.RemoveModel(model);
            table.Rows.Add(new TableRow(new TableCell(name, true), new TableCell(id, true), new TableCell(Centred(remove))));
        }

        return table;
    }

    private Button LoadModelsButton()
    {
        Button load = new() { Text = LOC.STR("Load from Server") };
        load.BindDataContext(control => control.Enabled, (AgentItemViewModel m) => m.CanLoadModels, DualBindingMode.OneWay);
        load.ToolTip = LOC.STR("Ask the server which models it has and add any that are missing.");
        CancellationTokenSource? inFlight = null;
        load.UnLoad += (_, _) => inFlight?.Cancel();
        load.Click += async (_, _) =>
        {
            using CancellationTokenSource stop = new();
            inFlight = stop;
            load.Enabled = false;
            ModelsResult? result = null;
            try
            {
                result = await Agent.FetchModelsAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                // The view went away while the server was answering, so there is nothing to show.
            }
            finally
            {
                inFlight = null;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                if (result is not null)
                    Agent.UseFetched(result);
                load.Enabled = Agent.CanLoadModels;
            });
        };
        return load;
    }

    private SectionBox ConnectionSection()
    {
        StatusGlyph glyph = new();
        glyph.ToneBinding.BindDataContext((AgentItemViewModel m) => m.ConnectionTone, DualBindingMode.OneWay);
        glyph.BindDataContext(control => control.ToolTip, (AgentItemViewModel m) => m.ConnectionText, DualBindingMode.OneWay);

        Control body = Agent.Connection switch
        {
            CliConnectionViewModel cli => CliBody(cli),
            ApiConnectionViewModel api => ApiBody(api),
            LocalConnectionViewModel local => LocalBody(local),
            _ => throw new InvalidOperationException("Unknown connection."),
        };
        body.DataContext = Agent.Connection;

        SectionBox box = new(LOC.STR("Connection"), null, Centred(glyph), body);
        box.IsExpandedBinding.Bind(Page, (AgentsPageViewModel m) => m.IsConnectionExpanded);
        return box;
    }

    private Control CliBody(CliConnectionViewModel cli)
    {
        Panel rows = new();
        void Rebuild() => rows.Content = PathRows(cli);
        Rebuild();
        cli.Paths.CollectionChanged += (_, _) => Rebuild();

        Button add = new() { Text = LOC.STR("Add Path") };
        add.Click += (_, _) =>
        {
            cli.Add(string.Empty);
            Application.Instance.AsyncInvoke(() => LastTextBox(rows)?.Focus());
        };
        Button browse = new() { Text = LOC.STR("Browse…") };
        browse.Click += (_, _) =>
        {
            using Eto.Forms.OpenFileDialog dialog = new() { Title = LOC.STR("Choose the App") };
            if (dialog.ShowDialog(this) == DialogResult.Ok)
                cli.Add(dialog.FileName);
        };

        Label literal = Note(LOC.STR("Rhino skips paths with ~, % or * for now. Use Browse to add the full path."));
        literal.TextColor = Palette.Warning;
        literal.BindDataContext(control => control.Visible, (CliConnectionViewModel m) => m.HasUnexpandedPaths, DualBindingMode.OneWay);

        return new TableLayout
        {
            Spacing = new Size(0, 6),
            Rows =
            {
                new TableRow(Note(LOC.STR("Rhino uses the first path that exists."))),
                new TableRow(rows),
                new TableRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, Items = { add, browse } }),
                new TableRow(literal),
            },
        };
    }

    private static Control PathRows(CliConnectionViewModel cli)
    {
        if (cli.Paths.Count == 0)
            return Note(LOC.STR("No search paths yet."));

        TableLayout table = new() { Spacing = new Size(6, 6) };
        foreach (SearchPathItemViewModel path in cli.Paths)
        {
            TextBox text = new() { DataContext = path, Font = Mono() };
            text.TextBinding.BindDataContext((SearchPathItemViewModel m) => m.Text);
            RemoveGlyphButton remove = new() { ToolTip = LOC.STR("Remove path") };
            remove.Click += (_, _) => cli.Remove(path);
            table.Rows.Add(new TableRow(new TableCell(text, true), new TableCell(Centred(remove))));
        }

        return table;
    }

    private static Control ApiBody(ApiConnectionViewModel api)
    {
        TextBox host = new() { PlaceholderText = "https://api.example.com", Font = Mono() };
        host.TextBinding.BindDataContext((ApiConnectionViewModel m) => m.Host);

        PasswordBox key = KeyBox(api);

        Button keyButton = new();
        keyButton.TextBinding.BindDataContext((ApiConnectionViewModel m) => m.KeyButtonText, DualBindingMode.OneWay);
        keyButton.BindDataContext(control => control.Enabled, (ApiConnectionViewModel m) => m.IsKeyButtonEnabled, DualBindingMode.OneWay);
        keyButton.BindDataContext(control => control.ToolTip, (ApiConnectionViewModel m) => m.KeyButtonToolTip, DualBindingMode.OneWay);
        keyButton.Click += (_, _) => api.PressKeyButton();

        Label keyStatus = Note();
        keyStatus.TextBinding.BindDataContext((ApiConnectionViewModel m) => m.KeyStatus, DualBindingMode.OneWay);

        return new TableLayout
        {
            Spacing = new Size(0, 6),
            Rows =
            {
                new TableRow(Heading(LOC.STR("Endpoint"))),
                new TableRow(host),
                new TableRow(new Panel { Padding = new Padding(0, 6, 0, 0), Content = Heading(LOC.STR("API key")) }),
                new TableRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(new TableCell(key, true), new TableCell(Centred(keyButton))) } }),
                new TableRow(keyStatus),
            },
        };
    }

    private static Control LocalBody(LocalConnectionViewModel local)
    {
        TextBox host = new() { PlaceholderText = "http://localhost:1234", Font = Mono() };
        host.TextBinding.BindDataContext((LocalConnectionViewModel m) => m.Host);

        return new TableLayout
        {
            Spacing = new Size(0, 6),
            Rows =
            {
                new TableRow(Heading(LOC.STR("Server URL"))),
                new TableRow(host),
            },
        };
    }

    // A saved key is never read back, so the box shows a stand-in until someone types a new one.
    private static PasswordBox KeyBox(ApiConnectionViewModel api)
    {
        PasswordBox key = new();
        bool isMasked = false;
        void Show()
        {
            if (key.HasFocus)
                return;

            isMasked = api.KeyText.Length == 0 && api.HasSavedKey;
            key.Text = isMasked ? SavedKeyMask : api.KeyText;
        }

        key.TextChanged += (_, _) =>
        {
            if (!isMasked)
                api.KeyText = key.Text;
        };
        key.GotFocus += (_, _) =>
        {
            if (!isMasked)
                return;

            isMasked = false;
            key.Text = string.Empty;
        };
        key.LostFocus += (_, _) => Show();
        api.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ApiConnectionViewModel.KeyText) or nameof(ApiConnectionViewModel.HasSavedKey))
                Show();
        };
        Show();
        return key;
    }

    private SectionBox PromptSection()
    {
        TextArea prompt = new()
        {
            Wrap = true,
            Height = PromptHeight,
            ToolTip = LOC.STR("Sent as a system prompt at the start of each conversation."),
        };
        prompt.TextBinding.BindDataContext((AgentItemViewModel m) => m.Prompt);

        SectionBox box = new(LOC.STR("Prompt"), null, null, prompt);
        box.IsExpandedBinding.Bind(Page, (AgentsPageViewModel m) => m.IsPromptExpanded);
        return box;
    }

    private Control RemoveRow()
    {
        Button remove = new() { Text = LOC.STR("Remove") };
        remove.BindDataContext(control => control.ToolTip, (AgentItemViewModel m) => m.RemoveToolTip, DualBindingMode.OneWay);
        remove.Click += (_, _) => AgentRemoval.Confirm(this, Page, Agent);
        return new TableLayout(new TableRow(new TableCell(null, true), new TableCell(remove)));
    }

    private static TextBox? LastTextBox(Panel rows) => rows.Children.OfType<TextBox>().LastOrDefault();

    private static Label Heading(string text) => new() { Text = text, Font = SystemFonts.Bold() };

    private static Label Note() => new() { TextColor = Palette.Secondary, Wrap = WrapMode.Word };

    private static Label Note(string text)
    {
        Label note = Note();
        note.Text = text;
        return note;
    }

    private static Font Mono() => Eto.Drawing.Fonts.Monospace(SystemFonts.Default().Size - 1);

    private static Control Centred(Control control) => new StackLayout
    {
        Orientation = Orientation.Horizontal,
        VerticalContentAlignment = VerticalAlignment.Center,
        Items = { control },
    };
}
#endif
