using Grasshopper2;
using Grasshopper2.Components;
using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;
using Grasshopper2.Parameters.Special;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;

namespace RhMcp.Resources;

public static class GH2_Utils
{

  public static bool TryGetDoc(out Document doc)
  {
    doc = default!;

    Editor editor = Editor.Instance;
    if (editor is null)
    {
      RhinoApp.InvokeAndWait(() => RhinoApp.RunScript("_G2", true));
      editor = Editor.Instance;
      if (editor is null) return false;
    }

    doc = editor.Canvas.Document;

    return doc is not null;
  }

  public static bool TryLoadDocument(string path)
  {
    if (!TryGetDoc(out _)) return false;
    return Editor.Instance.Documents.TryOpenDocument(path, OpenDocumentOptions.Default);
  }

  public static void Redraw()
  {
    var canvas = Editor.Instance?.Canvas;
    if (canvas is null) return;
    canvas.Invalidate();
  }

  public static string ClassifyKind(Type t)
  {
    if (t is null) return "Other";
    if (typeof(NumberSliderObject).IsAssignableFrom(t)) return "Slider";
    if (typeof(Component).IsAssignableFrom(t)) return "Component";
    if (typeof(IParameter).IsAssignableFrom(t)) return "Param";
    return "Other";
  }

  public static bool IsValueSource(IDocumentObject obj) =>
    obj.GetType().Namespace?.Contains("Parameters.Special") ?? false;

  private static Guid GH2_PlugInId { get; } = new Guid("8307876d-a461-4daa-bb77-eb3715925513");
  public static bool IsInstalled()
  {
    var plugIn = Rhino.PlugIns.PlugIn.Find(GH2_PlugInId);
    return plugIn is not null;
  }

}
