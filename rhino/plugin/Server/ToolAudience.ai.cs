using System.Threading;

namespace Rhino.AI.Server;

// Which route a call arrived on, for the one tool whose answer depends on who is asking: what is
// enabled is a per-assistant question. Null is the external route, which is not gated at all.
//
// AsyncLocal rather than a field: it flows with the request and no further, so two routes served at
// the same moment cannot read each other's answer.
internal static class ToolAudience
{
    private static AsyncLocal<AIProfile?> Asking { get; } = new();

    public static AIProfile? Profile
    {
        get => Asking.Value;
        set => Asking.Value = value;
    }
}
