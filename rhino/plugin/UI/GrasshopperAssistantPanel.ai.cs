using System.Runtime.InteropServices;

using Rhino.AI.UI;

namespace Rhino.AI;

// The Grasshopper assistant: the same panel as the general one, driving its own agent under the
// Grasshopper profile, so its conversation, agent choice, prompt and tool permissions are its own.
[Guid("7c2e5d3f-ae4b-4a7c-9d28-4f6b3eac8012")]
public class GrasshopperAssistantPanel : AIPanel
{
    public static new Guid PanelId => typeof(GrasshopperAssistantPanel).GUID;

    public GrasshopperAssistantPanel()
        : this(RhinoDoc.ActiveDoc is { } doc ? doc.RuntimeSerialNumber : 0u)
    {
    }

    public GrasshopperAssistantPanel(uint documentSerialNumber)
        : base(documentSerialNumber, AIProfile.Grasshopper)
    {
    }
}
