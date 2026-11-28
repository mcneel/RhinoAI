using System;
using System.Collections.Generic;
using System.Text.Json;

using Rhino.AI.PlugIns;
using Rhino.AI.Statistics;

namespace Rhino.AI;

internal class UserConversations
{

    public List<Conversation> Conversations { get; } = [];

}

internal class Conversation
{

    public Conversation(Guid conversationId, string agentName, List<ITurn> turns)
    {
        ConversationId = conversationId;
        AgentName = agentName;
        Turns = turns;
    }

    public Guid ConversationId { get; }

    public string AgentName { get; }

    private List<ITurn> Turns { get; }

}
