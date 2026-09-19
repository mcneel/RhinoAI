using System.Drawing;
using System.Globalization;

using Rhino.DocObjects;

namespace Rhino.AI.Tools;

[McpServerToolType]
internal static class SetLayerMaterialTool
{
    [McpServerTool("set_layer_material", "Set Layer Material", false, true)]
    [Description("Set the render material on a layer. Accepts diffuse color, transparency, and gloss. Optionally also sets the layer display color.")]
    public static IToolResult SetLayerMaterial(
        RhinoDoc doc,
        [Description("Layer full path")] string layer,
        [Description("Diffuse color hex like '#FF0000' or known color name")] string? color = null,
        [Description("Transparency 0.0 (opaque) to 1.0 (fully transparent)")] double? transparency = null,
        [Description("Glossiness 0.0 (matte) to 1.0 (mirror)")] double? gloss = null,
        [Description("Also apply color as the layer display (wireframe) color")] bool applyToLayerColor = true)
    {
        int idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
        if (idx < 0)
            return Failure(ToolError.RH_Layer_NotFound, $"Layer not found: {layer}");

        Color? parsedColor = ParseColor(color);
        if (color is not null && parsedColor is null)
            return Failure(ToolError.BadArgument, $"Could not parse color: {color}", "Use a hex string like '#FF0000' or a known color name");

        Layer lay = doc.Layers[idx];

        if (parsedColor.HasValue && applyToLayerColor)
            lay.Color = parsedColor.Value;

        int matIdx = lay.RenderMaterialIndex;
        if (matIdx < 0)
        {
            Material newMat = new() { Name = $"{lay.Name}_material" };
            if (parsedColor.HasValue)
                newMat.DiffuseColor = parsedColor.Value;
            matIdx = doc.Materials.Add(newMat);
            lay.RenderMaterialIndex = matIdx;
        }

        Material mat = doc.Materials[matIdx];

        Coercions coerced = new();

        if (parsedColor.HasValue)
            mat.DiffuseColor = parsedColor.Value;
        if (transparency.HasValue)
            mat.Transparency = coerced.Clamp("transparency", transparency.Value, 0.0, 1.0);
        if (gloss.HasValue)
            mat.Shine = coerced.Clamp("gloss", gloss.Value, 0.0, 1.0) * Material.MaxShine;

        mat.CommitChanges();
        doc.Views.Redraw();

        return Success(
            ContentBlock.CreateText($"Updated layer \"{layer}\" (material index {matIdx})."),
            coerced.Guidance);
    }

    private static Color? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        if (s.StartsWith("#", StringComparison.Ordinal))
        {
            string hex = s.Substring(1);
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
                return Color.FromArgb(r, g, b);
            }
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                int a = (int)((argb >> 24) & 0xFF);
                int r = (int)((argb >> 16) & 0xFF);
                int g = (int)((argb >> 8) & 0xFF);
                int b = (int)(argb & 0xFF);
                return Color.FromArgb(a, r, g, b);
            }
            return null;
        }

        Color named = Color.FromName(s);
        return named.IsKnownColor ? named : null;
    }
}
