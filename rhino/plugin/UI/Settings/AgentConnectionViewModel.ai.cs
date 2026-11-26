#if R9
using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal abstract class AgentConnectionViewModel : SimpleViewModel
{
    private protected AgentConnectionViewModel() { }

    public abstract AgentStatus Check { get; }

    public abstract ConnectionDraft ToDraft();

    public abstract AgentConnection ToSettings();
}
#endif
