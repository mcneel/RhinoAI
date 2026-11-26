using Rhino.Runtime;
using Rhino.UI;

namespace Rhino.AI.UI;

// RH-100340: named callbacks, so Rhino can hand the panel a prompt without referencing this plug-in.
internal static class HostPrompt
{
    private const string IsAvailableCallback = "Rhino.AI.IsAvailable";
    private const string PromptCallback = "Rhino.AI.Prompt";

    public static void Register()
    {
        HostUtils.RegisterNamedCallback(IsAvailableCallback, (_, e) => e.Set("result", IsAvailable()));
        HostUtils.RegisterNamedCallback(PromptCallback, (_, e) => e.Set("result", Prompt(e)));
    }

    private static bool IsAvailable() => AgentHost.AnyUsable;

    private static bool Prompt(NamedParametersEventArgs e)
    {
        if (!e.TryGetUnsignedInt("documentSerialNumber", out uint serial) || !e.TryGetString("prompt", out string prompt))
            return false;

        if (RhinoDoc.FromRuntimeSerialNumber(serial) is not { } doc)
            return false;

        Panels.OpenPanel(AIPanel.PanelId);
        return Panels.GetPanel<AIPanel>(doc) is { } panel && panel.PromptFromHost(prompt);
    }
}
