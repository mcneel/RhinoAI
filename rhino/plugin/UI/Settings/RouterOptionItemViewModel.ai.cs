using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class RouterOptionItemViewModel : SimpleViewModel
{
    private string text;

    public RouterOptionItemViewModel(RouterEnvOverride option, string value)
    {
        Option = option;
        text = value;
    }

    public RouterEnvOverride Option { get; }

    public string Label => Wording(Option.EnvVars[0])?.Label ?? Option.Label;

    public string Help => Wording(Option.EnvVars[0])?.Help ?? Option.Help;

    public string Placeholder => Option.Placeholder;

    public string Value
    {
        get => text;
        set
        {
            if (SetProperty(ref text, value))
                RaisePropertyChanged(nameof(IsSet));
        }
    }

    public bool IsSet => Option.IsSet(Value);

    private static (string Label, string Help)? Wording(string variable) => variable switch
    {
        "RHINO_MCP_DEFAULT_VERSION" => (LOC.STR("Rhino version to start"), LOC.STR("The version apps start when no Rhino has its MCP server running: 8, 9 or WIP.")),
        "RHINO_MCP_STARTUP_TIMEOUT" => (LOC.STR("Startup wait (seconds)"), LOC.STR("How long apps wait for Rhino to start. Raise it if Rhino starts slowly.")),
        "RHINO_MCP_TOOL_TIMEOUT" => (LOC.STR("Tool wait (seconds)"), LOC.STR("How long apps wait for a tool to finish. The tool keeps running in Rhino after that.")),
        "RHINO_MCP_RHINO_EXE_8" => (LOC.STR("Rhino 8 path"), LOC.STR("Apps start this Rhino 8 instead of the installed one.")),
        "RHINO_MCP_RHINO_EXE_9" => (LOC.STR("Rhino 9 and WIP path"), LOC.STR("Apps start this Rhino 9 or WIP instead of the installed one.")),
        _ => null,
    };
}
