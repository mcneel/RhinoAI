using Grasshopper2;
using Grasshopper2.UI;

namespace RhMcp.Resources;

public static class GH2_Utils
{

  public static bool TryGetDoc(out Grasshopper2.Doc.Document doc)
  {
    doc = default!;

    Editor editor = Editor.Instance;
    if (editor is null)
    {
      RhinoApp.InvokeAndWait(() => RhinoApp.RunScript("_G2", true));
      if (editor is null) return false;
    }

    doc = editor.Canvas.Document;

    return doc is not null;
  }

  public static bool TryLoadDocument(string path)
  {
    if (!TryGetDoc(out _)) return false;
    return Editor.Instance.Canvas.TryOpenDocument(path, Grasshopper2.UI.Canvas.OpenDocumentOptions.Default);
  }

  private static Guid GH2_PlugInId { get; } = new Guid("8307876d-a461-4daa-bb77-eb3715925513");
  public static bool IsInstalled()
  {
    var plugIn = Rhino.PlugIns.PlugIn.Find(GH2_PlugInId);
    return plugIn is not null;
  }

}