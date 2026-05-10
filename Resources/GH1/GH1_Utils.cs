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

}