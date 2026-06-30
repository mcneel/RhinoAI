using System.Collections.Generic;
using System.Linq;

namespace RhMcp;

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
    public static bool TryFor(RhinoDoc doc, out IAgentRunner agent)
    {
        if (!TryResolveActiveDefinition(doc, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }
        string docTitle = DocTitle(doc);
        agent = For(doc, () => AgentFactory.Create(def, docTitle));
        return true;
    }

    // Saved file name, else a stable placeholder so a transcript is still identifiable.
    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Name) ? "Untitled" : doc.Name;

    private static bool TryResolveActiveDefinition(RhinoDoc doc, out AgentDefinition def)
    {
        if (ActiveNames.TryGetValue(doc.RuntimeSerialNumber, out string? active) &&
            AgentRegistry.TryGet(active, out def))
            return true;

        return AgentRegistry.TryResolveActive(out def);
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
        if (!AgentRegistry.TryGet(dto.AgentName, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }

        (uint, string) key = (doc.RuntimeSerialNumber, def.Name);
        if (Agents.Remove(key, out IAgentRunner? prior))
            SafeDispose(prior);

        IAgentRunner resumed = AgentFactory.CreateResumed(def, dto);
        Agents[key] = resumed;
        SetActive(doc, def.Name);
        agent = resumed;
        return true;
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
