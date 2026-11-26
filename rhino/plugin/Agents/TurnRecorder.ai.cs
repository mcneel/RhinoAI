using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class TurnRecorder(Conversation conversation)
{
    private Conversation Conversation { get; } = conversation;

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

            case TurnEnd end:
                RecordEnd(end);
                break;
        }
    }

    public void Finish()
    {
        if (!HasEnded && LastSystemMessage is { Length: > 0 } message)
            Conversation.Record(TurnEventKind.AssistantText, message);
    }

    private void RecordEnd(TurnEnd end)
    {
        HasEnded = true;
        Conversation.RecordUsage(new TokenUsage(end.TokenCount ?? 0, 0));

        if (end.Success) return;

        string? reason = end.Data is { Length: > 0 } data ? data : LastSystemMessage;
        if (reason is { Length: > 0 })
            Conversation.Record(TurnEventKind.AssistantText, reason);
    }
}
