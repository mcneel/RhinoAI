using Rhino.UI;

using Rhino.AI.Models;
using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class LocalConnectionViewModel : AgentConnectionViewModel
{
    private string host;

    public LocalConnectionViewModel(string host, ProtocolKind protocol, string modelsRoute)
    {
        this.host = host;
        Protocol = protocol;
        ModelsRoute = modelsRoute;
    }

    public string Host
    {
        get => host;
        set
        {
            if (SetProperty(ref host, value))
                RaisePropertyChanged(nameof(Check));
        }
    }

    public string ModelsRoute { get; }

    private ProtocolKind Protocol { get; }

    public override AgentStatus Check => HostCheck.Of(host, LOC.STR("Enter the server URL so Rhino knows where to connect."))
        ?? new AgentStatus(AgentStatusKind.Ready, string.Format(LOC.STR("Rhino sends requests to {0}. Make sure the server is running when you chat."), host.Trim()));

    public override ConnectionDraft ToDraft() => new LocalConnectionDraft(host, Protocol, ModelsRoute);

    public override AgentConnection ToSettings() => new LocalConnection(host, Protocol, ModelsRoute);
}
