#if R9
using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal static class HostCheck
{
    public static AgentStatus? Of(string host, string whenBlank)
    {
        if (HttpAddress.IsValid(host))
            return null;

        return host.Trim().Length > 0
            ? new AgentStatus(AgentStatusKind.NeedsUrl, LOC.STR("Use a full URL that starts with http:// or https://."))
            : new AgentStatus(AgentStatusKind.NeedsUrl, whenBlank);
    }
}
#endif
