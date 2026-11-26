namespace Rhino.AI.UI.Settings;

internal abstract record ModelsResult;

internal sealed record ModelsFound(IReadOnlyList<ModelEntry> Models) : ModelsResult;

internal sealed record ModelsFailed(string Message) : ModelsResult;
