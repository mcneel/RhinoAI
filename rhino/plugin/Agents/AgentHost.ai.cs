using System.Collections.Generic;
using System.Linq;

namespace Rhino.AI;

internal static class AgentHost
{
    // Keyed by (document serial, agent name) so each doc can drive one agent per kind
    // (Claude, Codex, ...) at once without them colliding on a single slot.
    private static Dictionary<(uint Doc, string Name), IAgentRunner> Agents { get; } = new();

    // The active agent name per document. Absent => fall back to the configured default.
    private static Dictionary<uint, string> ActiveNames { get; } = new();

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    private static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        DisposeDoc(e.DocumentSerialNumber);
    }

    public static void SetActive(RhinoDoc doc, string name) =>
        ActiveNames[doc.RuntimeSerialNumber] = name;

    // The active agent for the doc, resolved via the registry and pooled per (doc, name).
    // Returns false (rather than null) when discovery finds nothing usable, so callers can
    // surface a friendly message instead of faulting.
    public static bool TryFor(RhinoDoc? doc, out IAgentRunner agent)
    {
        if (doc is null)
        {
            agent = default!;
            return false;
        }
        if (!TryResolveActiveDefinition(doc, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }
        agent = For(doc, () => def.GetRunner(DocTitle(doc)));
        return true;
    }

    // Saved file name, else a stable placeholder so a transcript is still identifiable.
    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Name) ? "Untitled" : doc.Name;

    private static bool TryResolveActiveDefinition(RhinoDoc doc, out AgentDefinition def)
    {
        if (ActiveNames.TryGetValue(doc.RuntimeSerialNumber, out string? active) &&
            AgentRegistry.Instance.TryGet(active, out def))
            return def.Enabled;

        string defaultAgentName = AISettings.DefaultAgentName;

        return AgentRegistry.Instance.TryGet(defaultAgentName, out def) && def.Enabled;
    }

    public static IAgentRunner For(RhinoDoc doc, Func<IAgentRunner> factory)
    {
        IAgentRunner probe = factory();
        (uint, string) key = (doc.RuntimeSerialNumber, probe.Name);
        if (Agents.TryGetValue(key, out IAgentRunner? existing))
        {
            SafeDispose(probe);
            return existing;
        }
        Agents[key] = probe;
        return probe;
    }

    // Adopt a runner restored from a past conversation as the doc's active agent for its kind,
    // replacing (and disposing) any runner already pooled for (doc, agent name) so the panel and
    // dispatch both pick up the resumed session. Pins the active name so dispatch resolves to it.
    // Returns false when the saved conversation's agent is no longer registered.
    public static bool TryResume(RhinoDoc doc, ConversationDto dto, out IAgentRunner agent)
    {
        if (!AgentRegistry.Instance.TryGet(dto.AgentName, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }

        if (!def.Enabled)
        {
            agent = default!;
            return false;
        }

        (uint, string) key = (doc.RuntimeSerialNumber, def.Name);
        if (Agents.Remove(key, out IAgentRunner? prior))
            SafeDispose(prior);

        IAgentRunner resumed = CreateResumed(def, dto);
        Agents[key] = resumed;
        SetActive(doc, def.Name);
        agent = resumed;
        return def.Enabled;
    }

    // Resume a persisted conversation: restore its transcript and seed the stream-json CLI to launch
    // with --resume <saved id> so the agent continues with its prior context. The runner drives the
    // restored Conversation, so the prior turns stay visible. Gemini (native ACP) has no --resume seam
    // here, so it falls back to a fresh native session while still showing the restored transcript.
    public static IAgentRunner CreateResumed(AgentDefinition def, ConversationDto dto)
    {
        Conversation restored = Conversation.Restore(dto);
        Guid resumeId = restored.AgentSessionId;
        switch (def.Name.ToLowerInvariant())
        {
            case "claude":
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new ClaudeStreamJsonParser(def), resumeId));
            case "codex":
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new CodexStreamJsonParser(def, CodexHome.Prepare()), resumeId));
            case "gemini":
                
                // No native --resume seam: the prior turns are shown for the user's reference, but the
                // fresh native session starts with no memory of them. Warn so the user doesn't assume
                // continuity the agent doesn't have.
                restored.NoteSystem("Gemini cannot resume prior context; the turns above are shown for reference only.");
                
                return new AgentRunner(def, restored, (client, _, cwd) => GeminiConnection.Connect(def, client, cwd));
            
            default:
                throw new NotImplementedException($"Unknown agent adapter {def.Name}");
        }
    }

    // The active pooled agent for the doc, so a control verb (cancel/stop) acts on the running
    // turn rather than an arbitrary idle agent the doc happened to drive earlier. Resolves the
    // active definition's name and looks up only that pooled entry; false when none is pooled.
    public static bool TryFindActive(RhinoDoc doc, out IAgentRunner agent)
    {
        if (TryResolveActiveDefinition(doc, out AgentDefinition def) &&
            Agents.TryGetValue((doc.RuntimeSerialNumber, def.Name), out IAgentRunner? existing))
        {
            agent = existing;
            return true;
        }
        agent = default!;
        return false;
    }

    public static void Stop(RhinoDoc doc)
    {
        DisposeDoc(doc.RuntimeSerialNumber);
    }

    public static void Drop(RhinoDoc doc, string name)
    {
        if (Agents.Remove((doc.RuntimeSerialNumber, name), out IAgentRunner? agent))
            SafeDispose(agent);
    }

    public static void Shutdown()
    {
        foreach (IAgentRunner agent in Agents.Values.ToArray())
            SafeDispose(agent);
        Agents.Clear();
        ActiveNames.Clear();
    }

    private static void DisposeDoc(uint serial)
    {
        foreach ((uint Doc, string Name) key in Agents.Keys.Where(k => k.Doc == serial).ToArray())
        {
            if (Agents.Remove(key, out IAgentRunner? agent))
                SafeDispose(agent);
        }
        ActiveNames.Remove(serial);
    }

    private static void SafeDispose(IAgentRunner agent)
    {
        try
        {
            agent.Dispose();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[agenthost] dispose failed: {ex.Message}");
        }
    }
}
