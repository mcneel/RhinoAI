using System.Collections.Generic;
using System.Linq;

using Rhino.AI.PlugIns;

namespace Rhino.AI;

internal static class AgentHost
{
    private static Dictionary<(uint Doc, Guid Id), AgentRunner> Runners { get; } = new();

    private static Dictionary<uint, Guid> ActiveIds { get; } = new();

    static AgentHost()
    {
        RhinoDoc.CloseDocument += OnCloseDocument;
    }

    private static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        DisposeDoc(e.DocumentSerialNumber);
    }

    public static IReadOnlyCollection<IAgentDefinition> Definitions => AIInstances.Agents.Definitions;

    public static bool IsUsable(IAgentDefinition definition) =>
        definition.Enabled && definition.Available && RhinoApp.IsInternetAccessAllowed;

    public static bool AnyUsable => Definitions.Any(IsUsable);

    // Transcripts saved before the SDK carry the agent's display name rather than its definition id.
    public static IAgentDefinition? Find(string key) =>
        Guid.TryParse(key, out Guid id)
            ? Definitions.FirstOrDefault(d => d.Id == id)
            : Definitions.FirstOrDefault(d => string.Equals(d.DisplayName, key, StringComparison.OrdinalIgnoreCase));

    public static void SetActive(RhinoDoc doc, Guid id) =>
        ActiveIds[doc.RuntimeSerialNumber] = id;

    private static Agent? WithOwnHarness(Agent agent)
    {
        if (agent.Model is not Rhino.AI.Models.DesktopModel)
            return agent.WithHarness(new RhinoHarness(PlugInRegistry.Register()!) { AskUser = PermissionPrompt.AskAsync });

        agent.Harness.AskUser = PermissionPrompt.AskAsync;
        return agent;
    }

    private static HashSet<uint> PanelDocs { get; } = new();

    public static void AttachPanel(uint doc) => PanelDocs.Add(doc);

    public static void DetachPanel(uint doc) => PanelDocs.Remove(doc);

    public static Conversation? PanelConversationFor(Agent agent)
    {
        foreach (KeyValuePair<(uint Doc, Guid Id), AgentRunner> entry in Runners)
            if (entry.Value.Runs(agent) && PanelDocs.Contains(entry.Key.Doc))
                return entry.Value.Conversation;
        return null;
    }

    public static bool TryFor(RhinoDoc? doc, out AgentRunner runner)
    {
        runner = default!;
        if (doc is null || ResolveActive(doc) is not { } definition)
            return false;

        (uint, Guid) key = (doc.RuntimeSerialNumber, definition.Id);
        if (Runners.TryGetValue(key, out AgentRunner? existing))
        {
            runner = existing;
            return true;
        }

        if (definition.GetAgent(PlugInRegistry.Register()!) is not { } agent || WithOwnHarness(agent) is not { } harnessed)
            return false;

        runner = AgentRunner.Start(definition, harnessed, DocTitle(doc));
        Runners[key] = runner;
        return true;
    }

    public static bool TryResume(RhinoDoc doc, ConversationDto dto, out AgentRunner runner)
    {
        runner = default!;
        if (Find(dto.AgentName) is not { } definition || !IsUsable(definition))
            return false;

        if (definition.GetAgent(PlugInRegistry.Register()!) is not { } agent || WithOwnHarness(agent) is not { } harnessed)
            return false;

        (uint, Guid) key = (doc.RuntimeSerialNumber, definition.Id);
        if (Runners.Remove(key, out AgentRunner? prior))
            SafeDispose(prior);

        runner = AgentRunner.Resume(definition, harnessed, dto);
        Runners[key] = runner;
        SetActive(doc, definition.Id);
        return true;
    }

    public static bool TryFindActive(RhinoDoc doc, out AgentRunner runner)
    {
        runner = default!;
        return ResolveActive(doc) is { } definition
            && Runners.TryGetValue((doc.RuntimeSerialNumber, definition.Id), out runner!);
    }

    public static void Stop(RhinoDoc doc)
    {
        DisposeDoc(doc.RuntimeSerialNumber);
    }

    public static void Shutdown()
    {
        foreach (AgentRunner runner in Runners.Values.ToArray())
            SafeDispose(runner);
        Runners.Clear();
        ActiveIds.Clear();
    }

    public static IAgentDefinition? ActiveDefinition(RhinoDoc? doc) =>
        doc is null ? FallbackDefinition() : ResolveActive(doc);

    private static IAgentDefinition? ResolveActive(RhinoDoc doc)
    {
        if (ActiveIds.TryGetValue(doc.RuntimeSerialNumber, out Guid active))
        {
            if (AIInstances.Agents.FromId(active) is { } pinned && IsUsable(pinned))
                return pinned;

            ActiveIds.Remove(doc.RuntimeSerialNumber);
        }

        return FallbackDefinition();
    }

    private static IAgentDefinition? FallbackDefinition() =>
        AIInstances.Agents.PreferredDefinition is { } preferred && IsUsable(preferred)
            ? preferred
            : Definitions.FirstOrDefault(IsUsable);

    private static string DocTitle(RhinoDoc doc) =>
        string.IsNullOrEmpty(doc.Name) ? "Untitled" : doc.Name;

    private static void DisposeDoc(uint serial)
    {
        foreach ((uint Doc, Guid Id) key in Runners.Keys.Where(k => k.Doc == serial).ToArray())
        {
            if (Runners.Remove(key, out AgentRunner? runner))
                SafeDispose(runner);
        }
        ActiveIds.Remove(serial);
    }

    private static void SafeDispose(AgentRunner runner)
    {
        try
        {
            runner.Dispose();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[agenthost] dispose failed: {ex.Message}");
        }
    }
}
