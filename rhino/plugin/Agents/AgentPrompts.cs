namespace RhMcp;

// Shared system-prompt steering for agents whose CLI lets us inject one (Claude, Codex). The
// built-in AskUserQuestion needs an interactive frontend we don't have in headless stdio mode, so
// steer every agent to the Rhino MCP tool that renders on the command line and in the panel instead.
internal static class AgentPrompts
{
    public static string AskUserSteer =>
        "To ask the user a question or have them choose between options, always call the "
        + $"mcp__{RouterMcpConfig.ServerName}__ask_user tool with the question and the options, then "
        + "STOP and end your turn. The tool does NOT return the answer: the user's answer arrives as "
        + "their next message, and you continue from there. Never use the built-in AskUserQuestion "
        + "tool: it cannot be displayed in this environment and will be cancelled.";

    // Grounding is pull-only: no document/canvas state is injected automatically, so the agent must
    // read current state before acting rather than assuming what is selected or open.
    public static string GroundingSteer =>
        "No document or selection state is injected automatically. Before acting on existing geometry "
        + "(moving, filleting, deleting, querying), first read the current state. For a quick orientation "
        + "call get_context once: it returns the current selection, the active viewport, and a doc/"
        + "Grasshopper summary in a single round-trip. Use the focused tools (get_selection, list_objects) "
        + "when you need more detail than the snapshot gives. Never assume which objects are selected, "
        + "which document is open, or what already exists. Pull the state and confirm before you act on it.";

    // The GH2 authoring loop is the headline flow: read the canvas, edit, solve, then act on the
    // structured per-component diagnostics that solve returns rather than assuming the graph is fine.
    public static string GrasshopperSteer =>
        "When authoring a Grasshopper (GH2) graph, work the loop, don't fire-and-forget: "
        + "1) read the current canvas with g2_get_canvas_graph before editing so you build on what is "
        + "already there; "
        + "2) build or modify the graph (g2_apply_graph places components/sliders and wires them, and "
        + "by default solves at the end); "
        + "3) solve with g2_solve_canvas, which returns {Solved, Phase, Errors, Warnings, Diagnostics[]} "
        + "where each diagnostic is {Id, Name, Nickname, Level (Remark|Warning|Error|Fault), Message} "
        + "(g2_apply_graph returns the same Diagnostics[] from its end-of-call solve); "
        + "4) READ BACK those diagnostics. If Solved is false or any diagnostic is an Error or Fault, "
        + "fix the offending components by Id and solve again; repeat until it solves clean, then report "
        + "what you built and any remaining warnings. "
        + "Prefer small incremental edits followed by a re-solve over assembling one large graph in a "
        + "single shot. It is far easier to localise a fault when each step is solved and checked.";

    // The always-on steers plus this agent's own prompt; the steers are never dropped.
    public static string Compose(string systemPrompt)
    {
        string steers = AskUserSteer + "\n\n" + GroundingSteer + "\n\n" + GrasshopperSteer;
        return systemPrompt.Length > 0 ? steers + "\n\n" + systemPrompt : steers;
    }
}
