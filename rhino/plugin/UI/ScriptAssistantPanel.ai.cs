using System.Runtime.InteropServices;

using Rhino.AI.UI;

namespace Rhino.AI;

// The Script Editor assistant: the same panel as the general one, driving its own agent under the
// Script profile, so its conversation, agent choice, prompt and tool permissions are its own.
//
// Not registered as a Rhino panel. ScriptEditorRail puts it inside the Script Editor's own window,
// which is where its work is, and a docked Rhino panel could never go there.
[Guid("6b1f4c2e-9d3a-4f6b-8c17-3e5a2d9b7f01")]
public class ScriptAssistantPanel : AIPanel
{
    public static new Guid PanelId => typeof(ScriptAssistantPanel).GUID;

    public ScriptAssistantPanel()
        : this(RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public ScriptAssistantPanel(uint documentSerialNumber)
        : base(documentSerialNumber, AIProfile.Script)
    {
    }
}
