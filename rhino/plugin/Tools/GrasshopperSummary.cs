namespace Rhino.AI.Tools;

internal static partial class GetContextTool
{
    public sealed record GrasshopperSummary(string Version, bool CanvasOpen, int ComponentCount, int WireCount);
}
