using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Rhino.AI.Resources;

internal static class GH1_Utils
{
  public static IEnumerable<IGH_Param> WireSources(GH_Document doc, IGH_Param input)
  {
    foreach (IGH_Param source in input.Sources)
    {
      IGH_Param? resolved = doc.FindParameter(source.InstanceGuid);
      if (resolved is not null)
        yield return resolved;
    }
  }

  public static bool TryGetOrCreateDoc(RhinoDoc rhDoc, out GH_Document doc)
  {
    doc = default!;
    if (Grasshopper.Instances.ActiveCanvas is null)
    {
      RhinoApp.RunScript(rhDoc.RuntimeSerialNumber, "_Grasshopper", true);
      if (Grasshopper.Instances.ActiveCanvas is null) return false;
    }

    var canvas = Grasshopper.Instances.ActiveCanvas;

    canvas.Document ??= new GH_Document();
    doc = canvas.Document;
    return doc is not null;
  }

  public static bool TryGetDoc(out GH_Document doc)
  {
    doc = default!;
    if (Grasshopper.Instances.ActiveCanvas is null) return false;
    doc = Grasshopper.Instances.ActiveCanvas.Document;
    return doc is not null;
  }

  public static void Redraw() => Grasshopper.Instances.RedrawCanvas();

  public static void ZoomExtents()
  {
    var canvas = Grasshopper.Instances.ActiveCanvas;
    if (canvas?.Document is null) return;
    var attrs = new List<IGH_Attributes>();
    foreach (IGH_DocumentObject obj in canvas.Document.Objects)
    {
      if (obj.Attributes is not null) attrs.Add(obj.Attributes);
    }
    if (attrs.Count == 0) return;
    canvas.Viewport.Focus(attrs);
    Grasshopper.Instances.RedrawCanvas();
  }

  public record struct Message(string Level, string Text);
  public record struct ComponentStatus(string Name, Message[] Messages);

  public record struct SolveSummary(bool Solved, int Objects, int Reported, ComponentStatus[] Statuses);

  public static SolveSummary Summarize(GH_Document ghDoc)
  {
    List<ComponentStatus> statuses = GetCanvasStatus(ghDoc);
    return new SolveSummary(statuses.Count == 0, ghDoc.ActiveObjects().Count, statuses.Count, statuses.ToArray());
  }

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
