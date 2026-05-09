using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;

using ModelContextProtocol.Server;

using Rhino;
using Rhino.DocObjects;

namespace RhMcp.Tools;

[McpServerToolType]
public static class SetLayerMaterialTool
{
    [McpServerTool(Name = "set_layer_material")]
    [Description("Set the render material on a layer. Accepts diffuse color, transparency, and gloss. Optionally also sets the layer display color.")]
    public static string SetLayerMaterial(
        RhinoDoc doc,
        [Description("Layer full path")] string layer,
        [Description("Diffuse color hex like '#FF0000' or known color name")] string? color = null,
        [Description("Transparency 0.0 (opaque) to 1.0 (fully transparent)")] double? transparency = null,
        [Description("Glossiness 0.0 (matte) to 1.0 (mirror)")] double? gloss = null,
        [Description("Also apply color as the layer display (wireframe) color")] bool applyToLayerColor = true)
    {
        var idx = doc.Layers.FindByFullPath(layer, RhinoMath.UnsetIntIndex);
        if (idx < 0) return $"Layer not found: {layer}";

        Color? parsedColor = ParseColor(color);
        if (color is not null && parsedColor is null)
            return $"Could not parse color: {color}";

        string result = string.Empty;

        RhinoApp.InvokeAndWait(() =>
        {
            var lay = doc.Layers[idx];

            if (parsedColor.HasValue && applyToLayerColor)
                lay.Color = parsedColor.Value;

            var matIdx = lay.RenderMaterialIndex;
            if (matIdx < 0)
            {
                var newMat = new Material { Name = $"{lay.Name}_material" };
                if (parsedColor.HasValue) newMat.DiffuseColor = parsedColor.Value;
                matIdx = doc.Materials.Add(newMat);
                lay.RenderMaterialIndex = matIdx;
            }

            var mat = doc.Materials[matIdx];

            if (parsedColor.HasValue) mat.DiffuseColor = parsedColor.Value;
            if (transparency.HasValue) mat.Transparency = Math.Clamp(transparency.Value, 0.0, 1.0);
            if (gloss.HasValue) mat.Shine = Math.Clamp(gloss.Value, 0.0, 1.0) * Material.MaxShine;

            mat.CommitChanges();
            doc.Views.Redraw();
            result = $"Updated layer \"{layer}\" (material index {matIdx}).";
        });

        return result;
    }

    private static Color? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        if (s.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = s.Substring(1);
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
                return Color.FromArgb(r, g, b);
            }
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                int a = (int)((argb >> 24) & 0xFF);
                int r = (int)((argb >> 16) & 0xFF);
                int g = (int)((argb >> 8) & 0xFF);
                int b = (int)(argb & 0xFF);
                return Color.FromArgb(a, r, g, b);
            }
            return null;
        }

        var named = Color.FromName(s);
        return named.IsKnownColor ? named : null;
    }
}
