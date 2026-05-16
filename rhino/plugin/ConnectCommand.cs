using System.IO;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoCommand = Rhino.Commands.Command;

namespace RhMcp;

public class ConnectCommand : RhinoCommand
{
    public override string EnglishName => "RhinoMCPConnect";

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
        Title = "Connect RhinoMCP to your AI Agent";
        
        Resizable = false;
        Padding = new Padding(12);
        Size = new Size(400, 260);

        TextArea textArea = new()
        {
            Text = Prompt(),
            ReadOnly = true,
            Wrap = true,
            Font = Fonts.Monospace(11),
        };

        Label blurb = new ()
        {
            Text = "Paste this prompt into your MCP-aware AI agent (e.g. Claude Code), it will handle the connection for you.",
            Wrap = WrapMode.Word,
        };

        Button copyButton = new () { Text = "Copy" };
        copyButton.Click += (_, _) =>
        {
            Clipboard.Instance.Text = textArea.Text;
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
                new TableRow(textArea) { ScaleHeight = true },
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
