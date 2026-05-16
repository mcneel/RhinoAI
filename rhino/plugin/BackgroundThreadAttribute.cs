namespace RhMcp;

// Marker for MCP tools that must NOT be marshalled to the Rhino UI thread.
// Default behaviour (no attribute) is main-thread dispatch via MainThreadFilter,
// because most tools touch Rhino/Grasshopper state and macOS aborts if AppKit
// is touched off the main thread. Apply this only to tools that are pure data
// or long-running CPU work where blocking the UI thread is undesirable.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class BackgroundThreadAttribute : Attribute { }
