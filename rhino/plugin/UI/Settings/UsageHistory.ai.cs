using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class UsageHistory
{
    public static IReadOnlyList<UsageTurn> Turns()
    {
        List<UsageTurn> turns = [];
        foreach (ConversationDto conversation in ConversationStore.List())
        {
            (string agentId, string agentName, string? vendor) = AgentOf(conversation.AgentName);
            foreach (TurnDto turn in conversation.Turns)
                turns.Add(new UsageTurn(turn.StartedAt, agentId, agentName, vendor, turn.Usage.InputTokens, turn.Usage.OutputTokens));
        }

        return turns;
    }

    private static (string Id, string Name, string? Vendor) AgentOf(string key)
    {
        if (AgentHost.Find(key) is IAgentDefinition definition)
            return (definition.Id.ToString(), definition.DisplayName, definition.Vendor);

        return (key, Guid.TryParse(key, out _) ? LOC.STR("Removed agent") : key, null);
    }
}
