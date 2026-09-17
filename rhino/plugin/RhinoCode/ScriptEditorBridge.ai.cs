#if R9

using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Rhino.PlugIns;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;
using Rhino.Runtime.Code.Storage;

namespace Rhino.AI;

internal readonly record struct ScriptRunOutcome(bool Success, string Stdout, string Stderr, string? Error);

// Rhino's Script Editor, driven the way ScriptEditorChat drove it. Rhino3DScriptEditor and the editor
// view-model live in RhinoCodePlatform.Rhino3D / RhinoCodeEditor, which ship with the RhinoCode plug-in
// rather than with us, so the entry point is bound reflectively and the model is used through `dynamic`.
// Code itself is in Rhino.Runtime.Code, which we reference. Every member runs on the UI thread, which
// is where ToolHandler puts a tool call.
internal sealed class ScriptEditorSession
{
    private static Type? ResolvedEditor { get; set; }

    private static Type? Editor => ResolvedEditor ??= ResolveEditor();

    // For callers that need the editor's own static members (its open/close events).
    internal static Type? EditorType => Editor;

    private readonly dynamic _model;

    private ScriptEditorSession(object model) => _model = model;

    private static Type? ResolveEditor()
    {
        try
        {
            if (!PlugIn.LoadPlugIn(ScriptingEnvironment.RhinoCodePluginId))
                return null;
            return Type.GetType("RhinoCodePlatform.Rhino3D.Rhino3DScriptEditor, RhinoCodePlatform.Rhino3D", throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static object? CurrentModel()
    {
        if (Editor?.GetMethod("TryGetModel", BindingFlags.Public | BindingFlags.Static) is not MethodInfo tryGet)
            return null;
        object?[] args = [null];
        return tryGet.Invoke(null, args) is true ? args[0] : null;
    }

    // Opens the editor as the ScriptEditor command would and waits for its model; a cold open starts
    // the languages, so it can take several seconds.
    public static async Task<ScriptEditorSession?> OpenAsync()
    {
        if (CurrentModel() is { } existing)
            return new ScriptEditorSession(existing);

        if (Editor?.GetMethod("OpenAsync", BindingFlags.Public | BindingFlags.Static, []) is MethodInfo open)
        {
            if (open.Invoke(null, null) is Task opening)
                _ = opening.ContinueWith(
                    static t => RhinoApp.WriteLine($"[rhino-ai] Script Editor did not open: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            RhinoApp.RunScript("-_ScriptEditor _Edit", echo: false);
        }

        for (int i = 0; i < 200; i++)
        {
            if (CurrentModel() is { } model)
                return new ScriptEditorSession(model);
            await Task.Delay(50);
        }
        return null;
    }

    // Closing the Script Editor only hides its window — the form and the view model stay alive — so
    // "has been opened" and "is on screen" are two different questions, and this is the second one.
    public static bool IsShowing => TryGetWindow(out Eto.Forms.Window window) && window.Visible;

    private const int ShowPollMs = 100;

    // A cold open loads the languages, which is the slow part and the part the editor reports on.
    private const int ShowPollTries = 600;

    /// <summary>The editor's window, on screen. Null when it did not come up.</summary>
    public static async Task<Eto.Forms.Window?> ShowWindowAsync()
    {
        if (TryGetWindow(out Eto.Forms.Window open))
            return Reveal(open);

        // The editor's own splash is opt-in and off by default (its General options, "Show banner"),
        // and the language-load progress it does show appears inside the window once that window is
        // up. The wait before that — loading the RhinoCode plug-in, preparing the languages — has no
        // UI of its own at all, so this is what says it is happening.
        RhinoApp.WriteLine(Opening);
        Rhino.UI.StatusBar.SetMessagePane(Opening);
        try
        {
            // The command rather than Rhino3DScriptEditor.OpenAsync: the same open either way, but
            // the command is the editor's own entry point, so it comes up exactly as it does for
            // someone who typed it, banner and all for whoever has one.
            RhinoApp.RunScript("_ScriptEditor", echo: false);

            // It returns as soon as the open is under way. This waits for the WINDOW, not the view
            // model, which exists from the first moment: a caller with something to put beside the
            // editor has nothing to attach to until the window is there.
            for (int i = 0; i < ShowPollTries; i++)
            {
                await Task.Delay(ShowPollMs).ConfigureAwait(true);
                if (TryGetWindow(out Eto.Forms.Window opened))
                    return Reveal(opened);
            }
            return null;
        }
        finally
        {
            Rhino.UI.StatusBar.ClearMessagePane();
        }
    }

    private const string Opening = "Opening the Script Editor…";

    // An editor that has been opened before can be perfectly alive and nowhere to be seen.
    private static Eto.Forms.Window Reveal(Eto.Forms.Window window)
    {
        if (window.WindowState == Eto.Forms.WindowState.Minimized)
            window.WindowState = Eto.Forms.WindowState.Normal;
        if (!window.Visible)
            window.Visible = true;
        window.BringToFront();
        return window;
    }

    // The editor's own window, so a caller can place it: MainEditor derives from Eto.Forms.Form.
    // False when the editor is not open.
    public static bool TryGetWindow(out Eto.Forms.Window window)
    {
        window = default!;
        if (Editor?.GetMethod("TryGetForm", BindingFlags.Public | BindingFlags.Static) is not MethodInfo tryGet)
            return false;

        object?[] args = [null];
        if (tryGet.Invoke(null, args) is not true)
            return false;

        if (args[0] is Eto.Forms.Window form)
        {
            window = form;
            return true;
        }
        return false;
    }

    // The open document's title and text WITHOUT opening the editor: for describing a call the user
    // has not agreed to yet, which must not have side effects of its own. UI thread.
    public static bool TryGetCurrentText(out string title, out string text)
    {
        title = string.Empty;
        text = string.Empty;

        if (CurrentModel() is not object model)
            return false;

        dynamic vm = model;
        Code? current = null;
        if (!vm.Codes.TryGetCurrent(out current))
            return false;

        // Pattern-matched rather than null-checked: the call above is dynamic, so the compiler cannot
        // carry a null state through it.
        if (current is not Code code)
            return false;

        title = code.Title ?? string.Empty;
        text = Text(code);
        return true;
    }

    public Code? Current()
    {
        Code? code = null;
        return _model.Codes.TryGetCurrent(out code) ? code : null;
    }

    public static string Text(Code code) => ((ICode)code).Text ?? string.Empty;

    public static void SetText(Code code, string text) => code.Text.Set(text);

    public static string? Path(Code code) => code.Uri is { } uri ? (uri.IsFile ? uri.LocalPath : uri.ToString()) : null;

    public static string Language(Code code) => ((ICode)code).LanguageSpec.ToString();

    public async Task<Code?> AddAsync(LanguageSpec spec, string text, string title)
    {
        if (Resolve(spec) is not ILanguage language)
            return null;
        Code created = language.CreateCode(text);
        created.Title = title;
        return await AddAsync(created) ? created : null;
    }

    public async Task<Code?> LoadAsync(LanguageSpec spec, string path)
    {
        if (Resolve(spec) is not ILanguage language)
            return null;

        Code? code = null;
        try
        {
            Code stored = language.CreateCode(new Uri(path));
            if (stored.TryLoad() || Text(stored).Length > 0)
                code = stored;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[rhino-ai] Script Editor could not bind {path}: {ex.Message}");
        }

        // Fall back to a detached copy of the file's text, so the script still reaches the editor.
        if (code is null)
        {
            code = language.CreateCode(File.ReadAllText(path));
            code.Title = System.IO.Path.GetFileName(path);
        }
        return await AddAsync(code) ? code : null;
    }

    private async Task<bool> AddAsync(Code code)
    {
        dynamic added = await _model.Codes.TryAdd(code);
        if (added is not true)
            return false;
        _model.Codes.TrySetCurrent(code);
        return true;
    }

    // With a path this is Save As and the document follows to the new file; without one the document's
    // own storage is rewritten. `bound` is false when the copy had to be written directly, in which
    // case the editor tab still points at its previous file.
    public static bool Save(Code code, string? path, out string location, out bool bound)
    {
        bound = true;
        if (path is null)
        {
            location = Path(code) ?? string.Empty;
            return code.HasStorage && code.TryStore();
        }

        string full = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(full) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
        location = full;

        if (StorageExtensions.TryGetStorage(new Uri(full), out IStorage storage) && code.TryStore(storage))
            return true;

        bound = false;
        File.WriteAllText(full, Text(code), new UTF8Encoding(false));
        return true;
    }

    // Deliberately not disposing the streams: the run context can flush after TryRun returns (deferred
    // output, scripts that leave callbacks printing) and a late write to a closed stream throws.
    public static ScriptRunOutcome Run(RhinoDoc doc, Code code)
    {
        Ensure(((ICode)code).LanguageSpec);

        MemoryStream stdout = new();
        MemoryStream stderr = new();
        RunContext context = new("Rhino AI", defaultOutputStream: false, defaultErrorStream: false)
        {
            AutoApplyParams = true,
            OutputStream = stdout,
            ErrorStream = stderr,
        };
        context.Inputs["__rhino_doc__"] = doc;

        bool success;
        string? error = null;
        try
        {
            success = code.TryRun(context, out ExecuteException exception);
            if (exception is not null)
                error = exception.Message;
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
        }

        return new ScriptRunOutcome(
            success,
            Encoding.UTF8.GetString(stdout.ToArray()),
            Encoding.UTF8.GetString(stderr.ToArray()),
            error);
    }

    private static ILanguage? Resolve(LanguageSpec spec)
    {
        Ensure(spec);
        return RhinoCode.Languages.QueryLatest(spec);
    }

    private static void Ensure(LanguageSpec spec)
    {
        if (spec == LanguageSpec.CSharp)
            ScriptingEnvironment.EnsureCSharpRuntimeIsAvailable();
        else
            ScriptingEnvironment.EnsurePythonRuntimeIsAvailable();
    }
}

#endif
