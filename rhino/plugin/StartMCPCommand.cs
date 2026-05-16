using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Rhino.Commands;

namespace RhMcp;

[CommandStyle(Style.ScriptRunner)] // Style.Hidden | 
public class StartMCPCommand : Command
{

    public override string EnglishName => "StartMCP";

    // Scripted callers sometimes invoke "_RhinoMCP <port>" which Rhino splits
    // into two commands — the port becomes an "Unknown command". Capturing the
    // command window lets us recover that intended port and forward it.
    private static readonly Regex UnknownPortPattern = new(@"Unknown command:\s*(\d+)", RegexOptions.Compiled);

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Task.Delay(500).ContinueWith(_ =>
        {
            RhinoApp.InvokeOnUiThread(() =>
            {
                string[] lines = RhinoApp.CommandHistoryWindowText.Split("\n").ToArray();
                int port = ParsePortFromHistory(lines) ?? RhinoMcpHost.GetNextPort();
                RhinoApp.RunScript($"_-RhinoMCP {port} _-Enter", false);
            });
        });

        return Result.Success;
    }

    private static int? ParsePortFromHistory(string[] lines)
    {
        bool sawRhinoMcp = false;
        foreach (string line in lines)
        {
            if (line.ToLowerInvariant().Contains("startmcp")) sawRhinoMcp = true;
            if (!sawRhinoMcp) continue;

            Match match = UnknownPortPattern.Match(line);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, out int port)) continue;
            if (port < 1 || port > 65535) continue;
            return port;
        }
        return null;
    }
}
