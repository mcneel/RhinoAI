namespace Rhino.AI;

// What Rhino has stopped and is waiting on the user for while a tool call is in flight, in the
// words the card should use — or null when nothing is waiting.
//
// One line for the whole application, because Rhino has one command line and one active getter.
// Written by UserPromptWatch and read by ToolSummary, which has to stay free of Rhino types: it is
// unit-tested in an assembly that never loads RhinoCommon, so the two cannot meet in a call.
// UI thread only.
internal static class ToolActivity
{
    public static string? Waiting { get; set; }
}
