using System.Text.Json.Serialization;

namespace Rhino.AI.UI;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ReadyCommand), "ready")]
[JsonDerivedType(typeof(CancelCommand), "cancel")]
[JsonDerivedType(typeof(NewConversationCommand), "conversation.new")]
[JsonDerivedType(typeof(LoadConversationCommand), "conversation.load")]
[JsonDerivedType(typeof(ResumeConversationCommand), "conversation.resume")]
[JsonDerivedType(typeof(ExitReviewCommand), "conversation.exitReview")]
[JsonDerivedType(typeof(SelectAgentCommand), "agent.select")]
[JsonDerivedType(typeof(LoginCommand), "agent.login")]
[JsonDerivedType(typeof(AnswerQuestionCommand), "question.answer")]
[JsonDerivedType(typeof(DismissQuestionCommand), "question.dismiss")]
[JsonDerivedType(typeof(ToolChipCommand), "tool.chip")]
[JsonDerivedType(typeof(PickAttachmentsCommand), "attachments.pick")]
[JsonDerivedType(typeof(SetZoomCommand), "zoom.set")]
[JsonDerivedType(typeof(OpenSettingsCommand), "settings.open")]
[JsonDerivedType(typeof(OpenUrlCommand), "url.open")]
[JsonDerivedType(typeof(ClipboardCommand), "clipboard.write")]
[JsonDerivedType(typeof(OpenMenuCommand), "menu.open")]
[JsonDerivedType(typeof(SetPermissionCommand), "permission.set")]
[JsonDerivedType(typeof(AnswerPermissionCommand), "permission.answer")]
internal abstract partial record PanelCommand { }

internal sealed record ReadyCommand : PanelCommand;
internal sealed record CancelCommand : PanelCommand;
internal sealed record NewConversationCommand : PanelCommand;
internal sealed record LoadConversationCommand(string SessionId) : PanelCommand;
internal sealed record ResumeConversationCommand(string SessionId) : PanelCommand;
internal sealed record ExitReviewCommand : PanelCommand;
internal sealed record SelectAgentCommand(string Name) : PanelCommand;
internal sealed record LoginCommand : PanelCommand;
internal sealed record AnswerQuestionCommand(IReadOnlyList<QuestionAnswer> Items) : PanelCommand;
internal sealed record DismissQuestionCommand(IReadOnlyList<string> Ids) : PanelCommand;
internal sealed record ToolChipCommand(string CallId, string ChipId) : PanelCommand;
internal sealed record PickAttachmentsCommand : PanelCommand;
internal sealed record SetZoomCommand(double Level) : PanelCommand;
// Page is the settings tab to land on, by label: the quick menu's link says "permissions", so
// opening permissions opens permissions.
internal sealed record OpenSettingsCommand(string? Page = null) : PanelCommand;

// Mode is "off" | "on" | "ask"; the host answers with a fresh `permissions` event.
internal sealed record SetPermissionCommand(string Name, string Mode) : PanelCommand;

// Remember turns a one-off answer into a standing permission: allowed becomes On, refused Off.
internal sealed record AnswerPermissionCommand(string Id, bool Allow, bool Remember) : PanelCommand;
internal sealed record OpenUrlCommand(string Url) : PanelCommand;
internal sealed record ClipboardCommand(string Text) : PanelCommand;

internal sealed record OpenMenuCommand(
    double X,
    double Y,
    bool CanZoomIn,
    bool CanZoomOut,
    bool CanResetZoom,
    string ZoomLabel,
    string Selection) : PanelCommand;

internal sealed record QuestionAnswer(string Id, IReadOnlyList<string> Answers);
