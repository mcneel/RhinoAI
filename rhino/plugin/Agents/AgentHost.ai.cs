using System.Collections.Generic;
using System.Linq;

namespace Rhino.AI;

internal static class AgentHost
{
    // Keyed by (document serial, profile, agent name): each panel drives its own agent of each kind
    // (Claude, Codex, ...) without colliding with the other panel's on the same document. An
    // application-wide profile uses Application in place of a document serial, so it has one agent of
    // each kind for the whole session rather than one per document.
    private static Dictionary<(uint Doc, AIProfile Profile, string Name), IAgentRunner> Agents { get; } = new();

    // The active agent name per (document, profile). Absent => the profile's configured default.
    private static Dictionary<(uint Doc, AIProfile Profile), string> ActiveNames { get; } = new();

    // No document has serial 0, so an application-wide agent is simply never matched by a document's
    // serial — which is what keeps it out of DisposeDoc when that document closes.
    private const uint Application = 0;

    // The assistants that belong to Rhino rather than to a document. The Script Editor is one window
    // for every open model and works on whichever is in front, so its assistant is one conversation
    // for them all; it would make no sense for it to forget what it was doing because the user
    // switched models, or to die with a model it was never about.
    public static bool IsApplicationWide(AIProfile profile) => profile == AIProfile.Script;

    private static uint KeyFor(RhinoDoc doc, AIProfile profile) =>
        IsApplicationWide(profile) ? Application : doc.RuntimeSerialNumber;

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    private static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        DisposeDoc(e.DocumentSerialNumber);
    }

    public static void SetActive(RhinoDoc doc, AIProfile profile, string name) =>
        ActiveNames[(KeyFor(doc, profile), profile)] = name;

    // The active agent for the doc and profile, resolved via the registry and pooled per
    // (doc, profile, name). Returns false (rather than null) when discovery finds nothing usable,
    // so callers can surface a friendly message instead of faulting.
    public static bool TryFor(RhinoDoc? doc, AIProfile profile, out IAgentRunner agent)
    {
        if (doc is null)
        {
            agent = default!;
            return false;
        }
        if (!TryResolveActiveDefinition(doc, profile, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }
        agent = For(doc, profile, () => def.GetRunner(profile, Title(doc, profile)));
        return true;
    }

    // The agents already going on this document, without starting one. For announcing state that is
    // not in any transcript (Rhino waiting at a getter), which must never be a reason to spawn a CLI.
    public static IReadOnlyList<IAgentRunner> Live(RhinoDoc doc) =>
        Agents.Where(entry => Concerns(entry.Key, doc)).Select(entry => entry.Value).ToArray();

    // An agent is this document's business if it is pooled for it, or if it is one of the
    // application-wide ones, which act on whatever document is in front.
    private static bool Concerns((uint Doc, AIProfile Profile, string Name) key, RhinoDoc doc) =>
        key.Doc == doc.RuntimeSerialNumber || key.Doc == Application;

    // The agent whose turn is running on this document, whichever panel started it; a tool that has
    // to reach "the conversation" (ask_user) goes through it. The AI panel's agent when nothing runs.
    public static bool TryForRunning(RhinoDoc doc, out IAgentRunner agent)
    {
        foreach (KeyValuePair<(uint Doc, AIProfile Profile, string Name), IAgentRunner> entry in Agents)
        {
            if (!Concerns(entry.Key, doc))
                continue;
            IReadOnlyList<Turn> turns = entry.Value.Conversation.Turns;
            if (turns.Count > 0 && !turns[^1].Completed)
            {
                agent = entry.Value;
                return true;
            }
        }
        return TryFor(doc, AIProfile.Rhino, out agent);
    }

    // What a transcript of this conversation is about. An application-wide assistant is about no one
    // document, so it says so rather than naming whichever happened to be in front when it started.
    private static string Title(RhinoDoc doc, AIProfile profile) =>
        IsApplicationWide(profile) ? AIProfiles.Name(profile)
        : string.IsNullOrEmpty(doc.Name) ? "Untitled" : doc.Name;

    private static bool TryResolveActiveDefinition(RhinoDoc doc, AIProfile profile, out AgentDefinition def)
    {
        if (ActiveNames.TryGetValue((KeyFor(doc, profile), profile), out string? active) &&
            AgentRegistry.Instance.TryGet(active, out def))
            return true;

        return AgentRegistry.Instance.TryGet(AISettings.DefaultAgentName(), out def);
    }

    public static IAgentRunner For(RhinoDoc doc, AIProfile profile, Func<IAgentRunner> factory)
    {
        IAgentRunner probe = factory();
        (uint, AIProfile, string) key = (KeyFor(doc, profile), profile, probe.Name);
        if (Agents.TryGetValue(key, out IAgentRunner? existing))
        {
            SafeDispose(probe);
            return existing;
        }
        Agents[key] = probe;
        return probe;
    }

    // Adopt a runner restored from a past conversation as the doc's active agent for its kind,
    // replacing (and disposing) any runner already pooled for (doc, profile, agent name) so the panel
    // and dispatch both pick up the resumed session. Pins the active name so dispatch resolves to it.
    // Returns false when the saved conversation's agent is no longer registered.
    public static bool TryResume(RhinoDoc doc, AIProfile profile, ConversationDto dto, out IAgentRunner agent)
    {
        if (!AgentRegistry.Instance.TryGet(dto.AgentName, out AgentDefinition def))
        {
            agent = default!;
            return false;
        }

        (uint, AIProfile, string) key = (KeyFor(doc, profile), profile, def.Name);
        if (Agents.Remove(key, out IAgentRunner? prior))
            SafeDispose(prior);

        IAgentRunner resumed = CreateResumed(profile, def, dto);
        Agents[key] = resumed;
        SetActive(doc, profile, def.Name);
        agent = resumed;
        return true;
    }

    // Resume a persisted conversation: restore its transcript and seed the stream-json CLI to launch
    // with --resume <saved id> so the agent continues with its prior context. The runner drives the
    // restored Conversation, so the prior turns stay visible. Gemini (native ACP) has no --resume seam
    // here, so it falls back to a fresh native session while still showing the restored transcript.
    public static IAgentRunner CreateResumed(AIProfile profile, AgentDefinition def, ConversationDto dto)
    {
        Conversation restored = Conversation.Restore(dto);
        Guid resumeId = restored.AgentSessionId;
        switch (def.Name.ToLowerInvariant())
        {
            case "claude":
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new ClaudeStreamJsonParser(def, profile), resumeId));
            case "codex":
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new CodexStreamJsonParser(def, profile, CodexHome.Prepare()), resumeId));
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

    // The active pooled agent for the doc and profile, so a control verb (cancel/stop) acts on the
    // running turn rather than an arbitrary idle agent the doc happened to drive earlier. Resolves
    // the active definition's name and looks up only that pooled entry; false when none is pooled.
    public static bool TryFindActive(RhinoDoc doc, AIProfile profile, out IAgentRunner agent)
    {
        if (TryResolveActiveDefinition(doc, profile, out AgentDefinition def) &&
            Agents.TryGetValue((KeyFor(doc, profile), profile, def.Name), out IAgentRunner? existing))
        {
            agent = existing;
            return true;
        }
        agent = default!;
        return false;
    }

    // Drops one panel's agents on the doc; the other panel's conversation is untouched.
    public static void Stop(RhinoDoc doc, AIProfile profile)
    {
        uint serial = KeyFor(doc, profile);
        foreach ((uint Doc, AIProfile Profile, string Name) key in Agents.Keys.Where(k => k.Doc == serial && k.Profile == profile).ToArray())
        {
            if (Agents.Remove(key, out IAgentRunner? agent))
                SafeDispose(agent);
        }
        ActiveNames.Remove((serial, profile));
    }

    public static void Drop(RhinoDoc doc, AIProfile profile, string name)
    {
        if (Agents.Remove((KeyFor(doc, profile), profile, name), out IAgentRunner? agent))
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
        foreach ((uint Doc, AIProfile Profile, string Name) key in Agents.Keys.Where(k => k.Doc == serial).ToArray())
        {
            if (Agents.Remove(key, out IAgentRunner? agent))
                SafeDispose(agent);
        }
        foreach ((uint Doc, AIProfile Profile) key in ActiveNames.Keys.Where(k => k.Doc == serial).ToArray())
            ActiveNames.Remove(key);
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
