using ModelContextProtocol.Protocol;

namespace RhMcp.Router;

/// <summary>
/// One contributed tool, and the slot that has it. The slot is not decoration: a call must go to
/// the Rhino whose plug-in registered the tool, not to whichever Rhino happens to be default.
/// </summary>
internal readonly record struct ContributedTool(Tool Tool, string SlotId);
