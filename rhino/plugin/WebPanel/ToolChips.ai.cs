using Rhino.UI;

namespace Rhino.AI.WebPanel;

// Authored host-side beside the card's title, because the panel holds no per-tool knowledge.
internal static class ToolChips
{
    public const string CancelId = "cancel";

    public static IReadOnlyList<PanelToolChip> None { get; } = [];

    private static IReadOnlyList<PanelToolChip> CancelOnly =>
        [new PanelToolChip(CancelId, LOC.STR("Cancel"), "stop", "danger")];

    public static IReadOnlyList<PanelToolChip> For(string toolName, bool isRunning) =>
        isRunning && IsCancellable(toolName) ? CancelOnly : None;

    private static bool IsCancellable(string toolName) => toolName is "run_command";
}
