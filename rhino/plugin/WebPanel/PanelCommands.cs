using System.Text.Json.Serialization;

namespace RhinoAI.WebPanel;

// The panel -> host half, matching PanelCommand in rhino/panel/src/protocol/events.ts.
//
// Deliberately a partial list: the panel already sends history, resume, undo, retry, context and
// attachment commands that this build does not implement yet. An unknown discriminator makes
// System.Text.Json throw, and PanelBridge treats that as "ignore and log" rather than a fault, so
// adding a case here is the only work needed to light one of them up.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ReadyCommand), "ready")]
[JsonDerivedType(typeof(PromptCommand), "prompt")]
[JsonDerivedType(typeof(CancelCommand), "cancel")]
[JsonDerivedType(typeof(NewConversationCommand), "conversation.new")]
[JsonDerivedType(typeof(LoadConversationCommand), "conversation.load")]
[JsonDerivedType(typeof(ResumeConversationCommand), "conversation.resume")]
[JsonDerivedType(typeof(ExitReviewCommand), "conversation.exitReview")]
[JsonDerivedType(typeof(SelectAgentCommand), "agent.select")]
[JsonDerivedType(typeof(AnswerQuestionCommand), "question.answer")]
[JsonDerivedType(typeof(DismissQuestionCommand), "question.dismiss")]
[JsonDerivedType(typeof(ToolChipCommand), "tool.chip")]
[JsonDerivedType(typeof(OpenSettingsCommand), "settings.open")]
[JsonDerivedType(typeof(OpenUrlCommand), "url.open")]
[JsonDerivedType(typeof(ClipboardCommand), "clipboard.write")]
[JsonDerivedType(typeof(OpenMenuCommand), "menu.open")]
internal abstract record PanelCommand;

internal sealed record ReadyCommand : PanelCommand;
internal sealed record PromptCommand(PromptRequest Request) : PanelCommand;
internal sealed record CancelCommand : PanelCommand;
internal sealed record NewConversationCommand : PanelCommand;
internal sealed record LoadConversationCommand(string SessionId) : PanelCommand;
internal sealed record ResumeConversationCommand(string SessionId) : PanelCommand;
internal sealed record ExitReviewCommand : PanelCommand;
internal sealed record SelectAgentCommand(string Name) : PanelCommand;
internal sealed record AnswerQuestionCommand(string Id, IReadOnlyList<string> Answers) : PanelCommand;
internal sealed record DismissQuestionCommand(string Id) : PanelCommand;
internal sealed record ToolChipCommand(string CallId, string ChipId) : PanelCommand;
internal sealed record OpenSettingsCommand : PanelCommand;
internal sealed record OpenUrlCommand(string Url) : PanelCommand;
internal sealed record ClipboardCommand(string Text) : PanelCommand;

// The panel reports what its menu items should look like rather than the host recomputing it, so
// the zoom ladder stays in one place.
internal sealed record OpenMenuCommand(
    double X,
    double Y,
    bool CanZoomIn,
    bool CanZoomOut,
    bool CanResetZoom,
    string ZoomLabel,
    string Selection) : PanelCommand;

internal sealed record PromptRequest(string Text);
