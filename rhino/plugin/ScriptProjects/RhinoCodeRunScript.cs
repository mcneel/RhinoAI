#if R9

using System.IO;
using System.Text;

using Rhino.AI.Tools;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;

namespace Rhino.AI.ScriptProjects;

internal class RhinoCodeRunScript : IRhinoCodeRunner
{

    public IToolResult RunScript(RhinoDoc doc, Lang lang, string script)
    {
        if (lang == Lang.Python3)
        {
            IToolResult result = ScriptingEnvironment.EnsurePythonRuntimeIsAvailable();
            if (result.IsFailure) return result;
        }
        else if (lang == Lang.CSharp)
        {
            IToolResult result = ScriptingEnvironment.EnsureCSharpRuntimeIsAvailable();
            if (result.IsFailure) return result;
        }

        LanguageSpec spec = lang switch
        {
            Lang.CSharp => LanguageSpec.CSharp,
            Lang.Python3 => LanguageSpec.Python3,

            _ => throw new NotImplementedException("Unknown Language")
        };
        
        SourceCode source = new(spec, script);
        if (!source.TryCreateCode(out Code code))
        {
            string guidance = string.Join(", ", code.Diagnostics.Select(d => d.Message)); // TODO : Line/column
            return Failure(ToolError.Failed, "Could not create code from the supplied script.", guidance);
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
        catch (Exception ex)
        {
            thrown = ex.Message;
        }

        string stdout = Encoding.UTF8.GetString(output.ToArray());
        string stderr = Encoding.UTF8.GetString(errors.ToArray());

        // Only a throw is a failure: a warning on stderr still means the script ran and its changes landed.
        if (thrown is not null)
            return Failure(ToolError.Failed, ContentBlock.CreateJson(new { stdout, stderr }), thrown);

        return Success(
            new { stdout, stderr },
            stderr.Length > 0 ? "The script wrote to stderr but ran to completion" : null);
    }
}

#endif
