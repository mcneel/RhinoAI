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
[JsonDerivedType(typeof(SelectAgentCommand), "agent.select")]
[JsonDerivedType(typeof(AnswerQuestionCommand), "question.answer")]
[JsonDerivedType(typeof(DismissQuestionCommand), "question.dismiss")]
[JsonDerivedType(typeof(OpenSettingsCommand), "settings.open")]
[JsonDerivedType(typeof(OpenUrlCommand), "url.open")]
[JsonDerivedType(typeof(ClipboardCommand), "clipboard.write")]
internal abstract record PanelCommand;

internal sealed record ReadyCommand : PanelCommand;
internal sealed record PromptCommand(PromptRequest Request) : PanelCommand;
internal sealed record CancelCommand : PanelCommand;
internal sealed record NewConversationCommand : PanelCommand;
internal sealed record SelectAgentCommand(string Name) : PanelCommand;
internal sealed record AnswerQuestionCommand(string Id, IReadOnlyList<string> Answers) : PanelCommand;
internal sealed record DismissQuestionCommand(string Id) : PanelCommand;
internal sealed record OpenSettingsCommand : PanelCommand;
internal sealed record OpenUrlCommand(string Url) : PanelCommand;
internal sealed record ClipboardCommand(string Text) : PanelCommand;

internal sealed record PromptRequest(string Text);
