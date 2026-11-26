using Eto.Drawing;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal enum StatusTone { Ok, Warn, Neutral }

internal enum AgentStatusKind { Ready, NotFound, NeedsKey, NeedsUrl, NeedsModel, Disabled, Offline, MacOnly, WindowsOnly }

internal sealed record AgentStatus(AgentStatusKind Kind, string Why);

internal static class AgentStatuses
{
    public static StatusTone Tone(AgentStatusKind kind) => kind switch
    {
        AgentStatusKind.Ready => StatusTone.Ok,
        AgentStatusKind.Disabled or AgentStatusKind.MacOnly or AgentStatusKind.WindowsOnly => StatusTone.Neutral,
        AgentStatusKind.NotFound or AgentStatusKind.NeedsKey or AgentStatusKind.NeedsUrl or AgentStatusKind.NeedsModel or AgentStatusKind.Offline => StatusTone.Warn,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string Label(AgentStatusKind kind) => kind switch
    {
        AgentStatusKind.Ready => LOC.STR("Ready"),
        AgentStatusKind.NotFound => LOC.STR("Not found"),
        AgentStatusKind.NeedsKey => LOC.STR("Needs a key"),
        AgentStatusKind.NeedsUrl => LOC.STR("Needs a URL"),
        AgentStatusKind.NeedsModel => LOC.STR("Needs a model"),
        AgentStatusKind.Disabled => LOC.STR("Disabled"),
        AgentStatusKind.Offline => LOC.STR("Offline"),
        AgentStatusKind.MacOnly => LOC.STR("Mac only"),
        AgentStatusKind.WindowsOnly => LOC.STR("Windows only"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static Color Colour(StatusTone tone) => tone switch
    {
        StatusTone.Ok => Palette.Allow,
        StatusTone.Warn => Palette.Warning,
        StatusTone.Neutral => Palette.SeriesOther,
        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, null),
    };
}
