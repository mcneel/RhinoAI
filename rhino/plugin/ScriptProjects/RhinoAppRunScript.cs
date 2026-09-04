using System.IO;
using System.Threading.Tasks;

namespace RhinoAI.ScriptProjects;

internal class RhinoAppRunScript : IRhinoCodeRunner
{

    public string RunScript(RhinoDoc doc, Lang lang, string script)
    {
        string ext = lang switch { Lang.Python3 => ".py", Lang.CSharp => ".cs", _ => ".txt" };
        string tmp = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(tmp, script);
        RhinoApp.CommandWindowCaptureEnabled = true;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"_-ScriptEditor _Run \"{tmp}\"", false);
        string[] lines = RhinoApp.CapturedCommandWindowStrings(true);
        RhinoApp.CommandWindowCaptureEnabled = false;

        _ = Task.Delay(15_000).ContinueWith(_ => { try { File.Delete(tmp); } catch { } });

        string[] filtered = (lines ?? [])
            .Where(l => !l.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int errIndex = Array.FindIndex(filtered, line => IsErrorLine(line, lang));

        string stdout;
        string? error;
        if (errIndex >= 0)
        {
            stdout = string.Concat(filtered.Take(errIndex));
            error = string.Concat(filtered.Skip(errIndex));
        }
        else
        {
            stdout = string.Concat(filtered);
            error = null;
        }

        return JsonSerializer.Serialize(new
        {
            stdout = stdout,
            error = error,
        });
    }

    // The script editor reports failures as ordinary command-window text, and a Python traceback shares no markers with a Roslyn error.
    private static bool IsErrorLine(string line, Lang lang)
    {
        if (line.StartsWith("Compile Error", StringComparison.OrdinalIgnoreCase))
            return true;

        return lang switch
        {
            Lang.Python3 => line.Contains("Traceback (most recent call last):", StringComparison.Ordinal),
            Lang.CSharp => line.Contains("error CS", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Exception:", StringComparison.Ordinal)
                || line.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
