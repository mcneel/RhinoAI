namespace RhMcp;

// Editable seed lists of model identifiers per adapter, used to populate the Model dropdown in AI
// settings. Not exhaustive and not validated: the CLIs accept any string and validate at request
// time, so these are conveniences. Models the user types are remembered separately (AISettings
// custom models) and merged on top of these.
internal static class KnownModels
{
    private static IReadOnlyList<string> ClaudeModels { get; } = ["opus", "sonnet", "haiku", "fable"];
    private static IReadOnlyList<string> CodexModels { get; } = ["gpt-5.5", "gpt-5"];
    private static IReadOnlyList<string> GeminiModels { get; } = ["gemini-2.5-pro", "gemini-2.5-flash"];

    public static IReadOnlyList<string> For(AgentAdapter adapter) => adapter switch
    {
        AgentAdapter.Claude => ClaudeModels,
        AgentAdapter.Codex => CodexModels,
        AgentAdapter.Gemini => GeminiModels,
        _ => [],
    };
}
