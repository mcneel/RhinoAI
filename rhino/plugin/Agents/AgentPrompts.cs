namespace Rhino.AI;

/// <summary>
/// Default Prompts for the AI Agent. These cannot be currentyl changed.
/// </summary>
internal static class AgentPrompts
{
    private static string Tool(string name) => $"mcp__{RouterMcpConfig.ServerName}__{name}";

    public static string AskUserSteer =>
        "To ask the user a question or have them choose between options, always call the "
        + $"{Tool("ask_user")} tool. On success, STOP and end your turn. On failure, follow its guidance and retry; do not wait for answers. It takes a "
        + "LIST of questions, so put every question you need answered into that ONE call rather than "
        + "asking them one turn at a time; they are shown together and answered together. The tool "
        + "does NOT return the answers: the user's reply arrives as their next message, and you "
        + "continue from there. Never use the built-in AskUserQuestion tool: it cannot be displayed "
        + "in this environment and will be canceled.";

    // Grounding is pull-only: no document/canvas state is injected automatically, so the agent must
    // read current state before acting rather than assuming what is selected or open.
    public static string GroundingSteer =>
        "No document or selection state is injected automatically. Before acting on existing geometry "
        + "(moving, filleting, deleting, querying), first read the current state. For a quick orientation "
        + $"call {Tool("get_context")} once: it returns the current selection, the active viewport, and a doc/"
        + $"Grasshopper summary in a single round-trip. Use the focused tools ({Tool("get_selection")}, {Tool("list_objects")}) "
        + "when you need more detail than the snapshot gives. Never assume which objects are selected, "
        + "which document is open, or what already exists. Pull the state and confirm before you act on it. "
        + $"When purely creating new geometry or layers (not referencing existing objects or the current "
        + $"selection), skip {Tool("get_context")} and execute {Tool("run_csharp")} directly in a single call. When capturing "
        + $"the viewport to view or render the scene, skip {Tool("get_context")} and call {Tool("get_viewport_image")} directly "
        + "(it already returns viewport, camera, and scene bounds metadata).";

    // The GH2 authoring loop is the headline flow: read the canvas, edit, solve, then act on the
    // structured per-component diagnostics that solve returns rather than assuming the graph is fine.
    public static string GrasshopperSteer =>
        "When authoring a Grasshopper (GH2) graph, work the loop, don't fire-and-forget: "
        + $"1) read the current canvas with {Tool("g2_get_canvas_graph")} before editing so you build on what is "
        + "already there; "
        + $"2) build or modify the graph ({Tool("g2_apply_graph")} places components/sliders and wires them, and "
        + "by default solves at the end); "
        + $"3) solve with {Tool("g2_solve_canvas")}, which returns {{Solved, Phase, Errors, Warnings, Diagnostics[]}} "
        + "where each diagnostic is {Id, Name, Nickname, Level (Remark|Warning|Error|Fault), Message} "
        + $"({Tool("g2_apply_graph")} returns the same Diagnostics[] from its end-of-call solve); "
        + "4) READ BACK those diagnostics. If Solved is false or any diagnostic is an Error or Fault, "
        + "fix the offending components by Id and solve again; repeat until it solves clean, then report "
        + "what you built and any remaining warnings. "
        + "Prefer small incremental edits followed by a re-solve over assembling one large graph in a "
        + "single shot. It is far easier to localize a fault when each step is solved and checked.";

    // C# runs in-process on Rhino's native .NET runtime, whereas Python 3 must cold-start RhinoCode's
    // CPython environment on first use and bridges RhinoCommon through pythonnet.
    public static string ScriptingSteer =>
        "When executing scripts or commands in Rhino, follow this order of preference: "
        + $"1) {Tool("run_csharp")} (default): use C# with the injected __rhino_doc__ (RhinoDoc) handle for creating "
        + "or modifying geometry, layers, materials, and querying RhinoCommon — it runs in-process on .NET "
        + "without spawning Python 3. Always use Console.WriteLine(...) to return output on stdout "
        + "(RhinoApp.WriteLine is not captured in stdout, and never throw an exception just to return output). "
        + "Coordinates and dimensions operate directly in active document units (`doc.ModelUnitSystem`). "
        + "When dimensions are unspecified, choose natural sizes for the document unit (e.g. 1–3 units if Feet or Meters; "
        + "25–100 units if Millimeters; 2–10 units if Inches). Only convert via RhinoMath.UnitScale(...) when the user "
        + "explicitly requests units differing from the document; "
        + $"2) {Tool("run_command")}: use when a native Rhino command macro is the most direct tool; "
        + $"3) {Tool("run_python")}: use only when the user explicitly asks for Python or requires a Python-specific library.";

    // Prevents agents that lack an AI image generator (such as Claude) from going on an unapproved
    // multi-step Rhino scene modification and _Render loop when asked to create an image or rendering.
    public static string ImageAndRenderingSteer =>
        "When the user asks to create an image, rendering, or visualization of the scene (e.g. requesting "
        + "a night shot, lighting, sky, or entourage in an image): "
        + $"1) If you have a built-in AI image generator (Codex image generation or Gemini image-generator "
        + $"subagent), skip `{Tool("get_context")}`, capture the current viewport directly with `{Tool("get_viewport_image")}` "
        + "(`width: 1280, height: 720` — it returns viewport/camera/scene metadata and displays the screen "
        + "capture in the panel), and generate the image without modifying the 3D Rhino document (do not "
        + $"add 3D entourage, lights, or environments to the .3dm file unless explicitly asked to model them "
        + $"in 3D, and do not use `{Tool("run_csharp")}` to read `imagegen/SKILL.md` — invoke `image_gen__imagegen` "
        + "directly). After generating the image, always report back a detailed **Scene Highlights** "
        + "breakdown with bullet points covering: **Lighting**, **Entourage & Materials**, **Sky & "
        + "Atmosphere**, and **Form & Perspective** (confirming the Rhino model's geometry and camera "
        + "framing were preserved and the .3dm file remains unchanged); "
        + "2) If you do NOT have an AI image generation tool (such as Claude), DO NOT modify the Rhino "
        + "scene, create 3D entourage/lights/environments/materials, create scratch preview viewports, "
        + "call Thread.Sleep, or run `_Render` on your own. Instead, immediately call the "
        + $"{Tool("ask_user")} tool to explain that you cannot generate AI images "
        + "like Gemini or Codex, and present clear options: 1) 'Switch to Gemini or Codex for AI image generation'; "
        + "2) 'Build 3D lights, entourage, and materials in the model, then run Rhino\\'s renderer'; or "
        + "3) 'Just capture the current viewport as a high-res Rendered screenshot without changing the scene'. "
        + "Never use compound/slash options like 'Rendered/Raytraced'. If a viewport screenshot is requested, "
        + $"call `{Tool("get_viewport_image")}` once with `displayMode: \"Rendered\"` for an instant capture. "
        + "Never run `_Render` or modify scene lighting/entourage without explicit user approval.";

    // The always-on steers plus this agent's own prompt; the steers are never dropped.
    public static string Compose(string systemPrompt)
    {
        string steers = AskUserSteer + "\n\n" + GroundingSteer + "\n\n" + ScriptingSteer + "\n\n" + ImageAndRenderingSteer + "\n\n" + GrasshopperSteer;
        return systemPrompt.Length > 0 ? steers + "\n\n" + systemPrompt : steers;
    }
}
