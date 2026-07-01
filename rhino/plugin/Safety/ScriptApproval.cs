using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

// Tool calls arrive on HTTP threads, so the prompt marshals to the UI thread and holds the call
// open until the user answers. Deny is both the default and the abort button: Enter and Esc are
// safe, running a script always takes a deliberate click.
internal static class ScriptApproval
{
    public static Task<bool> RequestAsync(string toolTitle, string body, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(false);

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.TrySetResult(Show(toolTitle, body, ct)); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }), null);
        return tcs.Task;
    }

    private static bool Show(string toolTitle, string body, CancellationToken ct)
    {
        TextArea script = new()
        {
            Text = body,
            ReadOnly = true,
            Wrap = false,
            Font = Fonts.Monospace(11),
        };

        Button deny = new() { Text = "Don't Run" };
        Button run = new() { Text = "Run" };

        Dialog<bool> dialog = new()
        {
            Title = "AI Script Approval",
            Padding = new Padding(12),
            MinimumSize = new Size(640, 420),
            Resizable = true,
        };
        deny.Click += (_, _) => dialog.Close(false);
        run.Click += (_, _) => dialog.Close(true);
        dialog.DefaultButton = deny;
        dialog.AbortButton = deny;

        dialog.Content = new TableLayout
        {
            Spacing = new Size(0, 8),
            Rows =
            {
                new TableRow(new Label { Text = $"The AI wants to use {toolTitle}. Review it, then choose." }),
                new TableRow(script) { ScaleHeight = true },
                new TableRow(new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Items = { null, deny, run },
                }),
            },
        };

        using CancellationTokenRegistration closeOnCancel = ct.Register(() =>
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try { dialog.Close(false); }
                catch (InvalidOperationException) { }
            }), null));

        return dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
    }
}
