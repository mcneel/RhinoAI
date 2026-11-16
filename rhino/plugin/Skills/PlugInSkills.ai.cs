using System.IO;

using Rhino.PlugIns;

namespace Rhino.AI.Skills;

/// <summary>
/// A skill a plugin ships, in the Agent Skills format (https://agentskills.io/specification):
/// its name and description from the front matter, and the whole SKILL.md. The same shape as the
/// SDK's <c>ISkill</c>, which this becomes once the plugin references the SDK.
/// </summary>
internal sealed record PlugInSkill(string Name, string Description, string SkillData, string PlugIn);

/// <summary>
/// The skills installed plugins ship: <c>skills/&lt;name&gt;/SKILL.md</c> in the plugin's package,
/// the folder holding its Yak <c>manifest.yml</c>, or beside the plugin when it has none.
/// </summary>
internal static class PlugInSkills
{

    // The line the SDK's Agent puts above its skills, so every agent reads the same words.
    public const string Heading =
        "The following skills are available. Call read_skill with a skill's name to load its full instructions before using it.";

    public static IReadOnlyList<PlugInSkill> Find()
    {
        List<PlugInSkill> found = [];
        HashSet<string> searched = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<Guid, string> plugIn in SafeInstalledPlugIns())
        {
            string? folder = PackageFolder(plugIn.Key);
            if (folder is null || !searched.Add(folder)) continue;
            found.AddRange(Read(folder, plugIn.Value));
        }
        return found;
    }

    public static PlugInSkill? Get(string name) =>
        Find().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The skills as the SDK's Agent lists them, or null when there are none.</summary>
    public static string? Prompt(IReadOnlyList<PlugInSkill> skills)
    {
        if (skills.Count == 0) return null;
        return Heading + "\n" + string.Concat(skills.Select(s => $"\n- {s.Name}: {s.Description}"));
    }

    /// <summary>The skills under a package folder's skills/, each folder named as its skill.</summary>
    internal static IEnumerable<PlugInSkill> Read(string packageFolder, string plugIn)
    {
        string root = Path.Combine(packageFolder, "skills");
        if (!Directory.Exists(root)) yield break;

        foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
        {
            string file = Path.Combine(folder, "SKILL.md");
            if (!File.Exists(file)) continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            if (ReadFrontMatter(text) is not { } front) continue;
            if (!front.TryGetValue("name", out string? name) || !front.TryGetValue("description", out string? description)) continue;
            // The spec ties a skill's name to its folder, so a skill is found by either.
            if (!string.Equals(name, Path.GetFileName(folder), StringComparison.Ordinal)) continue;

            yield return new PlugInSkill(name, description, text, plugIn);
        }
    }

    /// <summary>
    /// The top-level keys of a SKILL.md's YAML front matter with plain or quoted single-line
    /// values, which is all name and description take; null when the file has none.
    /// </summary>
    internal static Dictionary<string, string>? ReadFrontMatter(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return null;

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim() == "---") return values;
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith("#", StringComparison.Ordinal)) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);
            if (value.Length > 0) values[key] = value;
        }
        return null;
    }

    // A Yak package keeps its manifest.yml at its root, the plugin in it or a folder or two below.
    private static string? PackageFolder(Guid id)
    {
        string? path;
        try { path = PlugIn.PathFromId(id); }
        catch { return null; }
        if (string.IsNullOrEmpty(path)) return null;

        string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        string? start = folder;
        for (int up = 0; folder is not null && up < 3; up++)
        {
            if (File.Exists(Path.Combine(folder, "manifest.yml"))) return folder;
            folder = Path.GetDirectoryName(folder);
        }
        return start;
    }

    private static Dictionary<Guid, string> SafeInstalledPlugIns()
    {
        try { return PlugIn.GetInstalledPlugIns(); }
        catch { return []; }
    }

}
