namespace Rhino.AI.Router;

// How a spawned Rhino shows its main window. The spawned process is a full GUI
// Rhino in every mode; only the show state of its window differs, so nothing
// about document handling, viewport capture or plugin load changes with it.
//
// Windows only. CreateProcess carries the show state in STARTUPINFOW; the macOS
// launch path goes through `open -a`, which has no equivalent, so a non-Normal
// mode is ignored there.
public enum SpawnWindowMode
{
    // Let Rhino decide, which is what it did before this option existed.
    Normal,

    // Minimized and not activated, so the slot stays reachable from the taskbar
    // without stealing focus from whatever the user is doing.
    Minimized,

    // No window on the desktop or the taskbar at all. Intended for batch agent
    // runs, where a dozen slots would otherwise bury every other window.
    Hidden,
}
