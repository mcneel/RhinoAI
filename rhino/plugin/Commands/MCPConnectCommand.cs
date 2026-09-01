using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

public class MCPConnectCommand : RhinoCommand
{
    public override string EnglishName => "MCPConnect";

    protected override string CommandContextHelpUrl => "https://mcneel.github.io/RhinoMCP";

    protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, Rhino.Commands.RunMode mode)
    {
        ConnectDialog dialog = new();
        dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        return Rhino.Commands.Result.Success;
    }
}

internal sealed class ConnectDialog : Dialog
{
    // Advanced-tab fields, keyed by their env override, so Refresh can rebuild the
    // env dict from whatever the user has typed.
    private readonly Dictionary<RouterEnvOverride, TextBox> _fields = [];

    private readonly TextArea _promptTextArea;
    private readonly TextArea _jsonTextArea;

    // Repopulated each time the Install tab is shown so it reflects configs changed since it opened.
    private readonly Panel _installHost = new();

    public ConnectDialog()
    {
        Title = "Connect Rhino to your AI Agent";

        Resizable = true;
        Padding = new Padding(12);
        Size = new Size(460, 420);

        _promptTextArea = new TextArea
        {
            ReadOnly = true,
            Wrap = true,
            Font = Fonts.Monospace(11),
        };

        _jsonTextArea = new TextArea
        {
            ReadOnly = true,
            Wrap = false,
            Font = Fonts.Monospace(11),
        };

        TabControl tabs = new();
        TabPage installTab = new() { Text = "Install", Content = BuildInstall() };
        TabPage promptTab = new() { Text = "Prompt", Content = BuildPrompt() };
        TabPage jsonTab = new() { Text = "mcp.json", Content = Pad(_jsonTextArea) };
        tabs.Pages.Add(installTab);
        tabs.Pages.Add(promptTab);
        tabs.Pages.Add(jsonTab);
        tabs.Pages.Add(new TabPage { Text = "Advanced", Content = BuildAdvanced() });

        Button copyButton = new() { Text = "Copy", Enabled = false };
        copyButton.Click += (_, _) =>
        {
            TextArea active = tabs.SelectedPage == jsonTab ? _jsonTextArea : _promptTextArea;
            Clipboard.Instance.Text = active.Text;
            copyButton.Text = "Copied!";
        };

        // Copy only means anything on the two text tabs; re-scan the Install grid whenever it shows.
        tabs.SelectedIndexChanged += (_, _) =>
        {
            copyButton.Text = "Copy";
            copyButton.Enabled = tabs.SelectedPage == promptTab || tabs.SelectedPage == jsonTab;
            if (tabs.SelectedPage == installTab)
                PopulateInstall();
        };

        Button closeButton = new() { Text = "Close" };
        closeButton.Click += (_, _) => Close();

        StackLayout buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { null, copyButton, closeButton },
        };

        Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(tabs) { ScaleHeight = true },
                new TableRow(buttons),
            },
        };

        DefaultButton = closeButton;
        AbortButton = closeButton;

        Refresh();
    }

    // Every tab page's content gets the same inner padding so nothing sits flush against the frame.
    private static Control Pad(Control content) => new Panel { Padding = new Padding(10), Content = content };

    private Control BuildPrompt()
    {
        Label blurb = new()
        {
            Text = "If Install does not support your agent, you can paste this prompt into your MCP-aware AI agent (e.g. Claude), it will handle the connection for you.",
            Wrap = WrapMode.Word,
        };

        return Pad(new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(blurb),
                new TableRow(_promptTextArea) { ScaleHeight = true },
            },
        });
    }

    // Grid of MCP-aware agents: install the rhino server into a detected one with a click, or uninstall
    // it from a connected one. Backed by McpClientConfigInstaller, which also drove the old auto-install.
    private Control BuildInstall()
    {
        Label help = new()
        {
            Text = "Add or remove the Rhino MCP server for any agent detected on this machine. Detected agents "
                + "show an Install button; connected ones show an Uninstall button.",
            Wrap = WrapMode.Word,
            TextColor = Colors.Gray,
        };

        PopulateInstall();

        return Pad(new TableLayout
        {
            Spacing = new Size(0, 10),
            Rows =
            {
                new TableRow(help),
                new TableRow(_installHost) { ScaleHeight = true },
            },
        });
    }

    private void PopulateInstall()
    {
        TableLayout table = new() { Spacing = new Size(16, 8) };
        table.Rows.Add(new TableRow(
            new TableCell(HeaderLabel("Agent"), scaleWidth: true),
            new TableCell(HeaderLabel("Status")),
            new TableCell(HeaderLabel(""))));

        foreach (McpClientConfigInstaller.McpClient client in McpClientConfigInstaller.Clients)
        {
            McpClientConfigInstaller.McpInstallState state = McpClientConfigInstaller.GetState(client);
            table.Rows.Add(new TableRow(
                new TableCell(new Label { Text = client.DisplayName, VerticalAlignment = VerticalAlignment.Center }, scaleWidth: true),
                new TableCell(StatusLabel(state)),
                new TableCell(ActionControl(client, state))));
        }

        table.Rows.Add(null); // soak up spare vertical space
        _installHost.Content = new Scrollable { Content = table, Border = BorderType.None };
    }

    private static Control StatusLabel(McpClientConfigInstaller.McpInstallState state) => state switch
    {
        McpClientConfigInstaller.McpInstallState.Installed =>
            new Label { Text = "✓ Installed", TextColor = Colors.Green, VerticalAlignment = VerticalAlignment.Center },
        McpClientConfigInstaller.McpInstallState.Detected =>
            new Label { Text = "Detected", TextColor = Colors.Gray, VerticalAlignment = VerticalAlignment.Center },
        _ =>
            new Label { Text = "Not detected", TextColor = Colors.Gray, VerticalAlignment = VerticalAlignment.Center },
    };

    private Control ActionControl(McpClientConfigInstaller.McpClient client, McpClientConfigInstaller.McpInstallState state) => state switch
    {
        McpClientConfigInstaller.McpInstallState.Installed => UninstallButton(client),
        McpClientConfigInstaller.McpInstallState.Detected => InstallButton(client),
        _ => new Panel(),
    };

    private Button InstallButton(McpClientConfigInstaller.McpClient client)
    {
        Button button = new() { Text = "Install" };
        button.Click += (_, _) =>
        {
            McpClientConfigInstaller.McpInstallResult result = McpClientConfigInstaller.Install(client, CurrentEnv());
            if (result is McpClientConfigInstaller.McpInstallResult.Unsupported or McpClientConfigInstaller.McpInstallResult.Failed)
                MessageBox.Show(
                    this,
                    $"Couldn't add the Rhino MCP server to {client.DisplayName}. Its config may be in a shape we can't safely edit; "
                        + "use the mcp.json tab to add it by hand.",
                    "Install",
                    MessageBoxButtons.OK,
                    MessageBoxType.Warning);
            PopulateInstall();
        };
        return button;
    }

    private Button UninstallButton(McpClientConfigInstaller.McpClient client)
    {
        Button button = new() { Text = "Uninstall" };
        button.Click += (_, _) =>
        {
            McpClientConfigInstaller.McpUninstallResult result = McpClientConfigInstaller.Uninstall(client);
            if (result is McpClientConfigInstaller.McpUninstallResult.Unsupported or McpClientConfigInstaller.McpUninstallResult.Failed)
                MessageBox.Show(
                    this,
                    $"Couldn't remove the Rhino MCP server from {client.DisplayName}. Its config may be in a shape we can't safely edit; "
                        + "remove the rhino entry by hand.",
                    "Uninstall",
                    MessageBoxButtons.OK,
                    MessageBoxType.Warning);
            PopulateInstall();
        };
        return button;
    }

    private static Label HeaderLabel(string text) => new() { Text = text, Font = Fonts.Sans(11, FontStyle.Bold) };

    // One label + text field per override; editing any field rebuilds both tabs.
    private Control BuildAdvanced()
    {
        TableLayout table = new() { Spacing = new Size(8, 6) };

        foreach (RouterEnvOverride ov in RouterEnvOverride.Catalog)
        {
            TextBox field = new() { PlaceholderText = ov.Placeholder, ToolTip = ov.Help, Text = SeedValue(ov) };
            field.TextChanged += (_, _) => Refresh();
            _fields[ov] = field;

            Label label = new() { Text = ov.Label, ToolTip = ov.Help, VerticalAlignment = VerticalAlignment.Center };
            table.Rows.Add(new TableRow(label, new TableCell(field, scaleWidth: true)));
        }

        table.Rows.Add(null); // soak up spare vertical space
        return Pad(new Scrollable { Content = table, Border = BorderType.None });
    }

    private static string SeedValue(RouterEnvOverride ov)
    {
        if (ov == RouterEnvOverride.DefaultVersion && RhinoVersion.Token != ov.Default)
            return RhinoVersion.Token;

        if (RunningRhino.SourceBuildOverride is (string envVar, string path) && ov.EnvVars.Contains(envVar))
            return path;

        return string.Empty;
    }

    // Only fields the user changed from their default become env entries.
    private IReadOnlyDictionary<string, string> CurrentEnv()
    {
        Dictionary<string, string> env = [];
        foreach ((RouterEnvOverride ov, TextBox field) in _fields)
        {
            if (!ov.IsSet(field.Text))
                continue;

            string value = field.Text.Trim();
            foreach (string envVar in ov.EnvVars)
                env[envVar] = value;
        }
        return env;
    }

    private void Refresh()
    {
        IReadOnlyDictionary<string, string> env = CurrentEnv();
        _promptTextArea.Text = Prompt(env);
        _jsonTextArea.Text = RouterMcpConfig.BuildJson(env);
    }

    private static string Prompt(IReadOnlyDictionary<string, string> env) =>
$@"Install the Rhino MCP server. The entry is:

{RouterMcpConfig.EntryFragment(env)}

It is very important you tell the user to reload after this.";
}
