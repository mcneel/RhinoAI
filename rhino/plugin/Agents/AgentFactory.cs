using System;

namespace RhMcp;

internal static class AgentFactory
{
    public static IAgentRunner Create(AgentDefinition def, string docTitle) => def.Adapter switch
    {
        AgentAdapter.Claude => new AgentRunner(def, docTitle, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new ClaudeStreamJsonParser(def))),
        AgentAdapter.Codex => new AgentRunner(def, docTitle, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new CodexStreamJsonParser(def))),
        AgentAdapter.Gemini => new AgentRunner(def, docTitle, (client, _, cwd) => GeminiConnection.Connect(def, client, cwd)),
        _ => throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter."),
    };

    // Resume a persisted conversation: restore its transcript and seed the stream-json CLI to launch
    // with --resume <saved id> so the agent continues with its prior context. The runner drives the
    // restored Conversation, so the prior turns stay visible. Gemini (native ACP) has no --resume seam
    // here, so it falls back to a fresh native session while still showing the restored transcript.
    public static IAgentRunner CreateResumed(AgentDefinition def, ConversationDto dto)
    {
        Conversation restored = Conversation.Restore(dto);
        Guid resumeId = restored.AgentSessionId;
        switch (def.Adapter)
        {
            case AgentAdapter.Claude:
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new ClaudeStreamJsonParser(def), resumeId));
            case AgentAdapter.Codex:
                return new AgentRunner(def, restored, (client, convo, cwd) => new StreamJsonAgent(def, client, convo, cwd, new CodexStreamJsonParser(def), resumeId));
            case AgentAdapter.Gemini:
                // No native --resume seam: the prior turns are shown for the user's reference, but the
                // fresh native session starts with no memory of them. Warn so the user doesn't assume
                // continuity the agent doesn't have.
                restored.NoteSystem("Gemini cannot resume prior context; the turns above are shown for reference only.");
                return new AgentRunner(def, restored, (client, _, cwd) => GeminiConnection.Connect(def, client, cwd));
            default:
                throw new ArgumentOutOfRangeException(nameof(def), def.Adapter, "Unknown agent adapter.");
        }
    }
}
