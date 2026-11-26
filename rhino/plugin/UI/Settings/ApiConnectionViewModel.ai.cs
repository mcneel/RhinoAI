using Rhino.UI;

using Rhino.AI.Models;
using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class ApiConnectionViewModel : AgentConnectionViewModel
{
    private string host;
    private string? keyName;
    private KeySource savedSource;
    private string keyText = string.Empty;
    private bool isClearing;

    public ApiConnectionViewModel(string host, ProtocolKind protocol, string? keyName, KeySource savedSource, AgentsContext context, Func<string> agentName)
    {
        this.host = host;
        this.keyName = keyName;
        this.savedSource = savedSource;
        Protocol = protocol;
        Context = context;
        AgentName = agentName;
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

    public string KeyText
    {
        get => keyText;
        set
        {
            if (!SetProperty(ref keyText, value))
                return;

            if (value.Trim().Length > 0)
            {
                isClearing = false;
                if (string.IsNullOrWhiteSpace(keyName))
                    UseKeyName(AgentNames.KeyNameFor(AgentName()));
            }

            RaiseKey();
        }
    }

    public KeyChange Key => keyText.Trim().Length > 0 ? new SetKey(keyText) : isClearing ? new ClearKey() : new KeepKey();

    public bool HasSavedKey => savedSource != KeySource.None && !isClearing;

    public string KeyStatus => Key switch
    {
        SetKey => string.Format(IsMac ? LOC.STR("New key, saved to Keychain as {0} when you click OK.") : LOC.STR("New key, saved to Credential Manager as {0} when you click OK."), keyName),
        ClearKey => LOC.STR("Removed when you click OK."),
        KeepKey => savedSource switch
        {
            KeySource.Vault => IsMac ? LOC.STR("Stored in Keychain.") : LOC.STR("Stored in Credential Manager."),
            KeySource.Environment => string.Format(LOC.STR("From the {0} environment variable."), keyName),
            KeySource.None => LOC.STR("Not set. Paste a key to save it."),
            _ => throw new InvalidOperationException("Unknown key source."),
        },
        _ => throw new InvalidOperationException("Unknown key change."),
    };

    public string KeyButtonText => Key is KeepKey ? LOC.STR("Remove") : LOC.STR("Undo");

    public bool IsKeyButtonEnabled => Key is not KeepKey || savedSource == KeySource.Vault;

    public string KeyButtonToolTip => savedSource == KeySource.Environment && Key is KeepKey
        ? string.Format(LOC.STR("Rhino uses the {0} environment variable before any saved key. Change it there, then restart Rhino."), keyName)
        : Key is KeepKey ? LOC.STR("Remove the stored key when you click OK.") : LOC.STR("Keep the key Rhino has now.");

    public override AgentStatus Check => HostCheck.Of(host, LOC.STR("Enter the endpoint URL so Rhino knows where to send requests."))
        ?? (KeyWhy is string why ? new AgentStatus(AgentStatusKind.Ready, why) : new AgentStatus(AgentStatusKind.NeedsKey, LOC.STR("Set an API key so Rhino can connect.")));

    private ProtocolKind Protocol { get; }

    private AgentsContext Context { get; }

    private Func<string> AgentName { get; }

    private bool IsMac => Context.Os == HostOs.Mac;

    private string? KeyWhy => (Key, savedSource) switch
    {
        (_, KeySource.Environment) => string.Format(LOC.STR("Uses the key in the {0} environment variable."), keyName),
        (SetKey, _) => LOC.STR("Uses the new API key once you click OK."),
        (ClearKey, _) => null,
        (KeepKey, KeySource.Vault) => IsMac ? LOC.STR("Uses the API key stored in Keychain.") : LOC.STR("Uses the API key stored in Credential Manager."),
        (KeepKey, KeySource.None) => null,
        _ => throw new InvalidOperationException("Unknown key state."),
    };

    public void PressKeyButton()
    {
        if (Key is KeepKey)
        {
            isClearing = savedSource == KeySource.Vault;
            RaiseKey();
            return;
        }

        isClearing = false;
        if (keyText.Length > 0)
            KeyText = string.Empty;
        else
            RaiseKey();
    }

    public void FollowName(string previousName, string name)
    {
        if (keyName == AgentNames.KeyNameFor(previousName))
            UseKeyName(AgentNames.KeyNameFor(name));
    }

    public override ConnectionDraft ToDraft() => new ApiConnectionDraft(host, Protocol, keyName, Key);

    public override AgentConnection ToSettings() => new ApiConnection(host, Protocol, keyName, savedSource);

    private void UseKeyName(string name)
    {
        keyName = name;
        savedSource = Context.KeySourceOf(name);
        RaiseKey();
    }

    private void RaiseKey()
    {
        foreach (string property in new[] { nameof(Key), nameof(HasSavedKey), nameof(KeyStatus), nameof(KeyButtonText), nameof(IsKeyButtonEnabled), nameof(KeyButtonToolTip), nameof(Check) })
            RaisePropertyChanged(property);
    }
}
