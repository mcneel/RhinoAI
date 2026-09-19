using System.Text.Json.Serialization;

namespace Rhino.AI.UI;

[JsonDerivedType(typeof(PromptCommand), "prompt")]
[JsonDerivedType(typeof(OpenImageCommand), "image.open")]
[JsonDerivedType(typeof(SaveImageCommand), "image.save")]
internal abstract partial record PanelCommand { }

internal sealed record PromptCommand(PromptRequest Request) : PanelCommand;

internal sealed record OpenImageCommand(string Id) : PanelCommand;

internal sealed record SaveImageCommand(string Id) : PanelCommand;

internal sealed record PromptRequest(string Text, IReadOnlyList<PanelAttachment> Attachments);
