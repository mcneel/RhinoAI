using Rhino.Commands;

namespace Rhino.AI;

// Why the turn cannot be reverted. Permanent means the record is gone for good, so the panel can
// retire the button; otherwise it is a "not right now" the user can clear themselves.
internal readonly record struct RevertBlock(string Why, bool Permanent);

// "Revert this turn" = one Rhino undo of the record TurnUndoCheckpoint opened around it.
//
// Rhino's undo stack is linear, so this is only honest for the newest record in the document, and
// only once. A record the user has already undone with Ctrl+Z, or that Rhino has purged, is no
// longer on the stack: undoing again would take their own earlier work with it. Command.UndoRedo is
// the only way to see either happen, so it is watched from the first record we open onwards.
//
// UI thread only, which is where both the event and the panel's commands arrive.
internal static class TurnRevert
{
    // Records that must not be undone again. Keyed by record serial alone because the document only
    // reaches the event arguments on Rhino 9; two documents can reuse a serial, so this can mark a
    // record spent that belongs to the other one. That refuses a revert rather than performing a
    // wrong one, which is the right way to be wrong here.
    private static HashSet<uint> Spent { get; } = new();

    private static bool Watching { get; set; }

    public static void Watch()
    {
        if (Watching)
            return;
        Command.UndoRedo += OnUndoRedo;
        Watching = true;
    }

    private static void OnUndoRedo(object? sender, UndoRedoEventArgs e)
    {
        if (e.UndoSerialNumber == 0)
            return;

        if (e.IsBeginUndo || e.IsPurgeRecord)
            Spent.Add(e.UndoSerialNumber);
        else if (e.IsBeginRedo)
            Spent.Remove(e.UndoSerialNumber);
    }

    // Null when the turn can be reverted right now.
    public static RevertBlock? Blocked(RhinoDoc doc, uint record)
    {
        if (record == 0)
            return new RevertBlock("That turn made no undoable document changes.", true);

        if (Spent.Contains(record))
            return new RevertBlock("Those changes have already been undone.", true);

        // A record is open: the turn is still running, or a command is mid-flight. Undoing into
        // either one is not something to offer.
        if (doc.CurrentUndoRecordSerialNumber != 0)
            return new RevertBlock("Wait for the current operation to finish.", false);

        if (doc.NextUndoRecordSerialNumber != record + 1)
            return new RevertBlock("The document changed after that turn. Undo those changes first.", false);

        return null;
    }

    public static bool Revert(RhinoDoc doc) => doc.Undo();
}
