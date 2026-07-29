using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// Runs a tool invocation on the Rhino UI thread when the tool asks for it.
/// </summary>
/// <remarks>
/// macOS's AppKit aborts the process outright if a UI or document API is touched off the main
/// thread, and most tools reach into <c>RhinoDoc</c>. Compiled tools marshal by default and opt
/// out with <c>[BackgroundThread]</c>; contributed tools opt *in* through
/// <c>ProviderToolDescriptor.RequiresUiThread</c>, since a provider that already marshals
/// internally would otherwise pay for it twice.
/// <para>
/// <c>ToolHandler</c> applies the same policy inline for compiled tools — change both together.
/// </para>
/// </remarks>
internal static class UiThreadDispatch
{
    /// <summary>
    /// Invokes <paramref name="core"/>, on the Rhino UI thread when
    /// <paramref name="marshalToUi"/> is set and inline otherwise.
    /// </summary>
    /// <param name="marshalToUi">Whether the work must run on the UI thread.</param>
    /// <param name="core">The invocation to run.</param>
    /// <returns>
    /// The result of <paramref name="core"/>. An exception it throws is re-thrown to the
    /// caller rather than swallowed, so a provider that throws instead of returning an error
    /// result still surfaces at the dispatcher's error path.
    /// </returns>
    public static Task<CallToolResult> RunAsync(bool marshalToUi, Func<Task<CallToolResult>> core)
    {
        if (!marshalToUi)
            return core();

        // RunContinuationsAsynchronously keeps the awaiting request thread from resuming
        // inside RhinoApp's UI callback, which would hold the UI thread for the whole
        // remainder of the request.
        TaskCompletionSource<CallToolResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(async () =>
        {
            try
            { tcs.SetResult(await core().ConfigureAwait(false)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }
}
