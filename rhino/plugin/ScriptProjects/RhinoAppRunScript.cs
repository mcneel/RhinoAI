using System.IO;
using System.Text;
using System.Threading.Tasks;

using RhinoAI.ScriptProjects;

namespace RhinoAI.ScriptProjects;

internal class RhinoAppRunScript : IRhinoCodeRunner
{

    public string RunScript(RhinoDoc doc, Lang lang, string script)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"_-ScriptEditor _Run \"{tmp}\"", false);
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;

        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        RhinoApp.RunScript(doc.RuntimeSerialNumber, script, true);

        string[] filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int errIndex = Array.FindIndex(filtered, l =>
            l.StartsWith("Compile Error", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Exception:", StringComparison.Ordinal) ||
            l.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase));

        string stdout = "no output captured";
        string? error = null;
        if (errIndex >= 0)
        {
            stdout = string.Concat(filtered.Take(errIndex));
            error = string.Concat(filtered.Skip(errIndex));
        }

        return JsonSerializer.Serialize(new
        {
            stdout = stdout,
            error = error,
        });
    }
}
