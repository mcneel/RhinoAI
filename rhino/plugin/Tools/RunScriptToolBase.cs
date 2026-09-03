using System.IO;
using System.Text;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;

namespace RhinoAI.Tools;

internal static class RunScriptToolBase
{

    public enum Lang { Python3, CSharp }

    public static string RunScript(RhinoDoc doc, Lang lang, string script)
    {
        if (lang == Lang.Python3)
            ScriptingEnvironment.EnsurePythonRuntimeIsAvailable();
        else if (lang == Lang.CSharp)
            ScriptingEnvironment.EnsureCSharpRuntimeIsAvailable();

        LanguageSpec spec = lang switch
        {
            Lang.CSharp => LanguageSpec.CSharp,
            Lang.Python3 => LanguageSpec.Python3,

            _ => throw new NotImplementedException("Unknown Language")
        };
        
        SourceCode source = new(spec, script);
        if (!source.TryCreateCode(out Code code))
        {
            return JsonSerializer.Serialize(new { stdout = string.Empty, error = "Could not create code from the supplied script." });
        }

        using MemoryStream output = new();
        using MemoryStream errors = new();
        RunContext context = new(defaultOutputStream: false, defaultErrorStream: false)
        {
            // Inserts __rhino_doc__ etc.
            AutoApplyParams = true,
            OutputStream = output,
            ErrorStream = errors,
        };
        
        context.Inputs["__rhino_doc__"] = doc;

        string? thrown = null;
        try
        {
            code.Run(context);
        }
        catch (ExecuteException ex)
        {
            thrown = ex.Message;
        }

        string captured = Encoding.UTF8.GetString(errors.ToArray());
        return JsonSerializer.Serialize(new
        {
            stdout = Encoding.UTF8.GetString(output.ToArray()),
            error = captured.Length > 0 ? captured : thrown,
        });
    }
}
