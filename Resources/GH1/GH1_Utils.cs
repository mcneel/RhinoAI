using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Resources;

public static class GH1_Utils
{

  public static bool TryGetDoc(out GH_Document doc)
  {
    doc = default!;
    if (Instances.ActiveCanvas is null)
    {
      RhinoApp.RunScript("_Grasshopper", true);
      if (Instances.ActiveCanvas is null) return false;
    }

    doc = Instances.ActiveCanvas.Document;
    return doc is not null;
  }

  private static Guid GH1_PlugInId { get; } = new Guid("b45a29b1-4343-4035-989e-044e8580d9cf");
  public static bool IsInstalled()
  {
    var plugIn = Rhino.PlugIns.PlugIn.Find(GH1_PlugInId);
    return plugIn is not null;
  }

}