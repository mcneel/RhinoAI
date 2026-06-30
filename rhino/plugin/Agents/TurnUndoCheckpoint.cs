using System;
using System.Threading.Tasks;

namespace RhMcp;

// One undo record spanning a whole agent turn, so a single Ctrl+Z reverts every document mutation
// the turn made (geometry, scripted edits, GH changes) instead of one record per tool call. Pairs
// with auto-grant: the turn is the unit of work, so the turn is the unit of undo.
//
// The turn's tool calls arrive off-thread over HTTP and each marshals itself to the Rhino UI thread.
// RhinoDoc.BeginUndoRecord / EndUndoRecord must run on that same UI thread, so Open and Close marshal
// there too. Open before the prompt fires (the record must already be recording when the first tool
// call lands); Close in a finally so the record is ALWAYS ended, on completion, cancel, or fault.
// Leaving one open would swallow every later edit into a stale record.
internal sealed class TurnUndoCheckpoint
{
    // The exact doc the record was opened against, held by reference so Close ends it on the same
    // document identity rather than re-resolving by serial (serials can be reused across a session,
    // so a re-resolve could land on a different doc). Disposed once the doc closes, but we still
    // detect that explicitly below to surface a leak rather than swallow it.
    private RhinoDoc Doc { get; }

    // 0 means BeginUndoRecord declined (undo disabled, or a record was already open). We then own
    // nothing and must not call EndUndoRecord; whoever opened the existing record will close it.
    private uint RecordSerial { get; }

    private TurnUndoCheckpoint(RhinoDoc doc, uint recordSerial)
    {
        Doc = doc;
        RecordSerial = recordSerial;
    }

    public static Task<TurnUndoCheckpoint> OpenAsync(RhinoDoc doc, string description)
    {
        return OnUiThreadAsync(() =>
            new TurnUndoCheckpoint(doc, doc.BeginUndoRecord(description)));
    }

    public Task CloseAsync()
    {
        if (RecordSerial == 0)
            return Task.CompletedTask;

        return OnUiThreadAsync(() =>
        {
            // Resolve by serial only to learn whether the doc still exists; end against the captured
            // reference so we never end a record on a different doc that reused the serial. A gone
            // doc means a real record can't be ended (its undo stack is disposed with it), so say so
            // rather than silently dropping it: absence here is a leak, not "nothing to do".
            uint serial = Doc.RuntimeSerialNumber;
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is null)
            {
                RhinoApp.WriteLine($"[rhmcp] undo record {RecordSerial} could not be closed: doc {serial} is gone.");
                return true;
            }
            Doc.EndUndoRecord(RecordSerial);
            return true;
        });
    }

    // 'AI: ' + a single trimmed line of the prompt, capped, so the undo stack reads like
    // "Undo AI: extrude the selected curves" instead of a wall of text.
    public static string Describe(string prompt)
    {
        string firstLine = (prompt ?? string.Empty).Trim();
        int newline = firstLine.IndexOfAny(['\n', '\r']);
        if (newline >= 0)
            firstLine = firstLine[..newline].Trim();

        const int max = 60;
        if (firstLine.Length > max)
            firstLine = firstLine[..max].TrimEnd() + "…";

        return firstLine.Length == 0 ? "AI turn" : $"AI: {firstLine}";
    }

    private static Task<T> OnUiThreadAsync<T>(Func<T> work)
    {
        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }
}
