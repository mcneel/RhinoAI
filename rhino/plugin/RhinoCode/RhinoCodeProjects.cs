#if R9

using System.Text.RegularExpressions;

using Rhino.Runtime.Code.Display;
using Rhino.Runtime.Code.Projects;

namespace RhinoAI;

// Rhino3DCommandEditable lives in RhinoCodePlatform.Rhino3D, which ships with the RhinoCode
// plug-in rather than with us, so we never reference it. Image is only publicly settable on
// that derived type (ProjectCode.Image is protected), so the assignment is bound at runtime.
internal static class RhinoCodeProjects
{

    public static void SetIcon(ProjectCode code, string? svg)
    {
        if (string.IsNullOrEmpty(svg))
            return;

        try
        {
            dynamic editable = code;

            Svg lightSvg = new(svg);
            Svg darkSvg = new(ReplaceForDarkMode(svg));
            editable.Image = new SvgSet(lightSvg, darkSvg);
        }
        catch { }
    }

    private static string ReplaceForDarkMode(string svg)
    {
        // currentColor resolves to the 'color' property, whose initial value resvg treats as
        // black, so an icon that never declares 'color' needs its dark twin to name white
        // outright. When the document does declare 'color' we leave currentColor alone and let
        // the inversion of that declaration carry through instead.
        bool currentColorIsBlack = !ColorProperty.IsMatch(svg);

        return ColorValue.Replace(svg, match =>
        {
            Group value = match.Groups["value"];

            if (Invert(value.Value, currentColorIsBlack) is not string inverted)
                return match.Value;

            int start = value.Index - match.Index;
            return match.Value[..start] + inverted + match.Value[(start + value.Length)..];
        });
    }

    private static Regex ColorValue { get; } = new(
        """\b(?:fill|stroke|color|stop-color|flood-color|lighting-color)\s*[:=]\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[a-z]+\([^)]*\)|[^;"'\s>}/]+))""",
        RegexOptions.IgnoreCase);

    private static Regex ColorProperty { get; } = new(
        """(?<![-\w])color\s*[:=]""",
        RegexOptions.IgnoreCase);

    private static Regex BlackOrWhiteRgb { get; } = new(
        """^rgba?\(\s*(?<channel>0|255)\s*,\s*\k<channel>\s*,\s*\k<channel>\s*(?<alpha>,[^)]*)?\)$""",
        RegexOptions.IgnoreCase);

    private static Dictionary<string, string> Inversions { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "white",
        ["white"] = "black",
        ["#000"] = "#fff",
        ["#fff"] = "#000",
        ["#000f"] = "#ffff",
        ["#ffff"] = "#000f",
        ["#000000"] = "#ffffff",
        ["#ffffff"] = "#000000",
        ["#000000ff"] = "#ffffffff",
        ["#ffffffff"] = "#000000ff",
    };

    private static string? Invert(string color, bool currentColorIsBlack)
    {
        color = color.Trim();

        if (currentColorIsBlack && color.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            return "white";

        if (Inversions.TryGetValue(color, out string? swapped))
            return swapped;

        if (BlackOrWhiteRgb.Match(color) is { Success: true } rgb)
        {
            string channel = rgb.Groups["channel"].Value == "0" ? "255" : "0";
            Group alpha = rgb.Groups["alpha"];

            return alpha.Success
                ? string.Format("rgba({0}, {0}, {0}{1})", channel, alpha.Value)
                : string.Format("rgb({0}, {0}, {0})", channel);
        }

        return null;
    }

}

#endif
