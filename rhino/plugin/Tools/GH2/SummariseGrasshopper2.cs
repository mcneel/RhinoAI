using System.Runtime.CompilerServices;
using Rhino.AI.Resources;
using static Rhino.AI.Tools.GetContextTool;

namespace Rhino.AI.Tools;

internal static class Grasshopper2
{
    
    // This method is in a separate class due to GH2 Assembly loading and resolving
    // This little attribute ENSURES this method is kept as a separate class and not moved into the caller method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static GrasshopperSummary Summarize()
    {
        if (!GH2_Utils.TryPeekDoc(out global::Grasshopper2.Doc.Document ghDoc))
            return new GrasshopperSummary("GH2", false, 0, 0);

        int components = 0;
        int wires = 0;
        foreach (global::Grasshopper2.Doc.IDocumentObject obj in ghDoc.Objects.Forwards)
        {
            components++;
            if (obj is global::Grasshopper2.Components.Component comp)
            {
                foreach (global::Grasshopper2.Parameters.IParameter input in comp.Parameters.Inputs)
                    wires += GH2_Utils.WireSources(ghDoc, input).Count();
            }
            else if (obj is global::Grasshopper2.Parameters.IParameter param)
            {
                wires += GH2_Utils.WireSources(ghDoc, param).Count();
            }
        }

        return new GrasshopperSummary("GH2", true, components, wires);
    }

}
