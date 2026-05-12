using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace RhMcp.Resources;

public static class GH1_Utils
{

  public static bool TryGetOrCreateDoc(out GH_Document doc)
  {
    doc = default!;
    if (Instances.ActiveCanvas is null)
    {
      RhinoApp.InvokeAndWait(() => RhinoApp.RunScript("_Grasshopper", true));
      if (Instances.ActiveCanvas is null) return false;
    }

    var canvas = Instances.ActiveCanvas;

    RhinoApp.InvokeAndWait(() =>
    {
      canvas.Document ??= new GH_Document();
    });
    doc = canvas.Document;
    return doc is not null;
  }

  public static bool TryGetDoc(out GH_Document doc)
  {
    doc = default!;
    if (Instances.ActiveCanvas is null) return false;
    doc = Instances.ActiveCanvas.Document;
    return doc is not null;
  }

  public static void Redraw() => Instances.RedrawCanvas();

  public static void ZoomExtents()
  {
    var canvas = Instances.ActiveCanvas;
    if (canvas?.Document is null) return;
    var attrs = new List<IGH_Attributes>();
    foreach (IGH_DocumentObject obj in canvas.Document.Objects)
    {
      if (obj.Attributes is not null) attrs.Add(obj.Attributes);
    }
    if (attrs.Count == 0) return;
    canvas.Viewport.Focus(attrs);
    Instances.RedrawCanvas();
  }

  public record struct Message(string Level, string Text);
  public record struct ComponentStatus(string Name, Message[] Messages);

  public static List<ComponentStatus> GetCanvasStatus(GH_Document ghDoc)
  {
    var statuses = new List<ComponentStatus>();
    foreach (IGH_ActiveObject obj in ghDoc.ActiveObjects())
    {
      var messages = new List<Message>();
      foreach (var m in obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
        messages.Add(new Message(GH_RuntimeMessageLevel.Warning.ToString(), m));
      foreach (var m in obj.RuntimeMessages(GH_RuntimeMessageLevel.Error))
        messages.Add(new Message(GH_RuntimeMessageLevel.Error.ToString(), m));
      if (messages.Count == 0) continue;
      statuses.Add(new ComponentStatus(obj.Name, messages.ToArray()));
    }
    return statuses;
  }

  public static string ClassifyKind(Type t)
  {
    if (t is null) return "Other";
    if (typeof(GH_NumberSlider).IsAssignableFrom(t)) return "Slider";
    if (typeof(IGH_Component).IsAssignableFrom(t)) return "Component";
    if (typeof(IGH_Param).IsAssignableFrom(t)) return "Param";
    return "Other";
  }

  public static bool IsValueSource(IGH_DocumentObject obj) =>
    obj is GH_NumberSlider
    || obj is GH_MultiDimensionalSlider
    || obj is GH_Panel
    || obj is GH_ValueList
    || obj is GH_BooleanToggle
    || obj is GH_ButtonObject
    || obj is GH_ColourSwatch;

  private static Guid GH1_PlugInId { get; } = new Guid("b45a29b1-4343-4035-989e-044e8580d9cf");
  public static bool IsInstalled()
  {
    var plugIn = Rhino.PlugIns.PlugIn.Find(GH1_PlugInId);
    return plugIn is not null;
  }

}