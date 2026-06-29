using System.IO;
using System.Runtime.InteropServices;
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
        var dialog = new ConnectDialog();
        dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        return Rhino.Commands.Result.Success;
    }
}

internal sealed class ConnectDialog : Dialog
{

    public ConnectDialog()
    {
        Title = "Connect Rhino to your AI Agent";
        
        Resizable = false;
        Padding = new Padding(12);
        Size = new Size(400, 300);

        TextArea promptTextArea = new()
        {
            Text = Prompt(),
            ReadOnly = true,
            Wrap = true,
            Font = Fonts.Monospace(11),
        };

        TextArea jsonTextArea = new()
        {
            Text = McpJson(),
            ReadOnly = true,
            Wrap = false,
            Font = Fonts.Monospace(11),
        };

        Label blurb = new ()
        {
            Text = "Paste this prompt into your MCP-aware AI agent (e.g. Claude), it will handle the connection for you.",
            Wrap = WrapMode.Word,
        };

        TabControl tabs = new ();
        TabPage promptTab = new () { Text = "Prompt", Content = promptTextArea };
        TabPage jsonTab = new () { Text = "mcp.json", Content = jsonTextArea };
        tabs.Pages.Add(promptTab);
        tabs.Pages.Add(jsonTab);

        Button copyButton = new () { Text = "Copy" };
        copyButton.Click += (_, _) =>
        {
            TextArea active = tabs.SelectedPage == jsonTab ? jsonTextArea : promptTextArea;
            Clipboard.Instance.Text = active.Text;
            copyButton.Text = "Copied!";
        };

        Button closeButton = new () { Text = "Close" };
        closeButton.Click += (_, _) => Close();

        StackLayout buttons = new ()
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
                new TableRow(blurb),
                new TableRow(tabs) { ScaleHeight = true },
                new TableRow(buttons),
            },
        };

        DefaultButton = closeButton;
        AbortButton = closeButton;
    }

    private static string Prompt() =>
$@"Install the Rhino MCP server. The entry is:

""rhino"": {{ ""command"": ""{RouterPath()}"" }}

Then tell the user to reload";

    private static string McpJson()
    {
        string escapedPath = RouterPath().Replace("\\", "\\\\");
        return
$@"{{
  ""mcpServers"": {{
    ""rhino"": {{
      ""command"": ""{escapedPath}""
    }}
  }}
}}";
    }

    private static string RouterPath()
    {
        string pluginDir = Path.GetDirectoryName(typeof(RhMcpPlugin).Assembly.Location) ?? string.Empty;
        string routerRoot = Path.GetFullPath(Path.Combine(pluginDir, "..", "router"));
        string rid = GetRid();
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rhino-mcp-router.exe" : "rhino-mcp-router";
        return Path.Combine(routerRoot, rid, exe);
    }

    private static string GetRid()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
        return $"linux-{arch}";
    }

}
