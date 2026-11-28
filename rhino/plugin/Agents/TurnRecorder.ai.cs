using Rhino.UI;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class TurnRecorder(Conversation conversation, string agentName)
{
    private Conversation Conversation { get; } = conversation;

    private string AgentName { get; } = agentName;

    // CLI harnesses report stderr and sign-in failures as system messages, and only the last one explains a turn that died.
    private string? LastSystemMessage { get; set; }

    private bool HasEnded { get; set; }

    public void Record(ITurn turn)
    {
        switch (turn)
        {
            case MessageTurn { Role: RoleType.Assistant } message:
                Conversation.Record(TurnEventKind.AssistantText, message.Data);
                break;

            case MessageTurn { Role: RoleType.System } message:
                LastSystemMessage = message.Data;
                break;

            case ToolTurn tool:
                Conversation.Record(TurnEventKind.ToolUse, tool.Name, args: ToolArgs.ToJson(tool.Args).ToJsonString(), id: tool.Id);
                break;

            case ToolResultTurn result:
                Conversation.CompleteToolCall(result.Id, result.Data, failed: !result.Success);
                break;

            case UsageTurn usage:
                Conversation.RecordUsage(new TokenUsage(usage.InputTokens, usage.OutputTokens));
                break;

            case TurnEnd end:
                RecordEnd(end);
                break;
        }
    }

    public void Finish()
    {
        if (!HasEnded && LastSystemMessage is { Length: > 0 } message)
            Conversation.FailTurn(message);
    }

    private void RecordEnd(TurnEnd end)
    {
        HasEnded = true;

        switch (end.Reason)
        {
            case StopReason.InvalidSession:
                Conversation.MarkSessionLost(string.Format(LOC.STR("{0} no longer has this conversation, so it can't be continued. Start a new conversation to keep working."), AgentName));
                return;

            case StopReason.RanOutOfTokens:
                Conversation.FailTurn(LOC.STR("Ran out of output tokens while thinking. Raise this model's max output tokens in AI settings."));
                return;

            case StopReason.MaxTokens:
                Conversation.FailTurn(LOC.STR("Reply was cut off at the max output token limit."));
                return;
        }

        if (end.Success) return;

        string? reason = end.Data is { Length: > 0 } data ? data : LastSystemMessage;
        if (reason is { Length: > 0 })
            Conversation.FailTurn(reason);
    }
}
