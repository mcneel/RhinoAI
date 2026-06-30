using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;

namespace RhMcp.Tools;

// One-shot grounding snapshot: selection + active viewport + doc/Grasshopper
// summary in a single round-trip, so the agent can orient before acting without
// chaining get_selection / list_objects / view calls. Pull-only, read-only.
[McpServerToolType]
public static class GetContextTool
{
    public sealed record SelectedObject(string Id, string Name, string Layer, string Type);

    public sealed record CameraSummary(
        double[] Location,
        double[] Target,
        double[] Up,
        double LensLength,
        string Projection);

    public sealed record ViewportSummary(string Name, string DisplayMode, CameraSummary Camera);

    public sealed record DocSummary(int ObjectCount, int LayerCount);

    public sealed record GrasshopperSummary(bool CanvasOpen, int ComponentCount, int WireCount);

    public sealed record ContextSnapshot(
        SelectedObject[] Selection,
        ViewportSummary? ActiveViewport,
        DocSummary Document,
        GrasshopperSummary Grasshopper,
        // Per-section failures, so one throwing section never nukes the snapshot.
        string[]? Warnings);

    [McpServerTool("get_context", "Get Context Snapshot", true, false)]
    [Description("One round-trip grounding snapshot of current state: the active-doc selection (ids/types/layers), the active viewport (name + camera summary), a doc summary (object/layer counts), and a Grasshopper summary (component/wire count if a canvas is open). Read-only; pulls everything you need to orient before acting.")]
    public static string GetContext(RhinoDoc doc)
    {
        List<string> warnings = [];

        SelectedObject[] selection = Try(() => SelectionOf(doc), [], "selection", warnings);
        ViewportSummary? viewport = Try(() => SummarizeViewport(doc), null, "viewport", warnings);
        DocSummary document = Try(() => SummarizeDocument(doc), new DocSummary(0, 0), "document", warnings);
        GrasshopperSummary grasshopper = Try(SummarizeGrasshopper, new GrasshopperSummary(false, 0, 0), "grasshopper", warnings);

        ContextSnapshot snapshot = new(
            selection,
            viewport,
            document,
            grasshopper,
            warnings.Count == 0 ? null : [.. warnings]);

        return JsonSerializer.Serialize(snapshot, McpSerializer.Options);
    }

    // Shared selection projection: also the source of truth for GetSelectionTool's
    // wire shape. Guards the layer lookup per-object so one stale layer index can't
    // throw away the whole selection.
    public static SelectedObject[] SelectionOf(RhinoDoc doc) => doc.Objects
        .GetSelectedObjects(includeLights: false, includeGrips: false)
        .Select(o => new SelectedObject(
            o.Id.ToString(),
            o.Name ?? string.Empty,
            LayerPath(doc, o.Attributes.LayerIndex),
            o.Geometry?.GetType().Name ?? "Unknown"))
        .ToArray();

    private static string LayerPath(RhinoDoc doc, int layerIndex) =>
        layerIndex >= 0 && layerIndex < doc.Layers.Count
            ? doc.Layers[layerIndex].FullPath
            : string.Empty;

    // Shared viewport-to-summary projection: also the source of truth for
    // GetViewportImageTool's camera metadata.
    public static ViewportSummary? SummarizeViewport(RhinoDoc doc)
    {
        RhinoView? view = doc.Views.ActiveView;
        return view is null ? null : SummarizeViewport(view.ActiveViewport);
    }

    public static ViewportSummary SummarizeViewport(RhinoViewport vp)
    {
        CameraSummary camera = new(
            XYZ(vp.CameraLocation),
            XYZ(vp.CameraTarget),
            XYZ((Point3d)vp.CameraUp),
            vp.Camera35mmLensLength,
            vp.IsPerspectiveProjection ? "perspective"
                : vp.IsParallelProjection ? "parallel"
                : "two-point-perspective");

        return new ViewportSummary(vp.Name ?? string.Empty, vp.DisplayMode?.EnglishName ?? string.Empty, camera);
    }

    private static T Try<T>(Func<T> section, T fallback, string name, List<string> warnings)
    {
        try
        {
            return section();
        }
        catch (Exception ex)
        {
            warnings.Add($"{name}: {ex.Message}");
            return fallback;
        }
    }

    private static DocSummary SummarizeDocument(RhinoDoc doc)
    {
        ObjectEnumeratorSettings settings = new()
        {
            ActiveObjects = true,
            HiddenObjects = true,
            LockedObjects = true,
            DeletedObjects = false,
            IncludeLights = true,
            IncludeGrips = false,
        };

        int objectCount = doc.Objects.GetObjectList(settings).Count();
        return new DocSummary(objectCount, doc.Layers.Count);
    }

    // GH1 only: it is the canvas compiled in every Rhino target (GH2 is R9-only and
    // excluded from this build). A one-line component/wire count, no per-object detail.
    private static GrasshopperSummary SummarizeGrasshopper()
    {
        GH_Document? ghDoc = Instances.ActiveCanvas?.Document;
        if (ghDoc is null)
            return new GrasshopperSummary(false, 0, 0);

        int components = 0;
        int wires = 0;
        foreach (IGH_DocumentObject obj in ghDoc.Objects)
        {
            components++;
            if (obj is IGH_Component comp)
            {
                foreach (IGH_Param input in comp.Params.Input)
                    wires += input.Sources.Count;
            }
            else if (obj is IGH_Param param)
            {
                wires += param.Sources.Count;
            }
        }

        return new GrasshopperSummary(true, components, wires);
    }

    public static double[] XYZ(Point3d p) => [p.X, p.Y, p.Z];
}
