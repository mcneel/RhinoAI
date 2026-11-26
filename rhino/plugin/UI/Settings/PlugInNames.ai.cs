#if R9
namespace Rhino.AI.UI.Settings;

internal sealed class PlugInNames
{
    private IReadOnlyDictionary<Guid, string> Installed { get; }

    private PlugInNames(IReadOnlyDictionary<Guid, string> installed) => Installed = installed;

    public static PlugInNames Current() => new(Rhino.PlugIns.PlugIn.GetInstalledPlugIns());

    public bool IsInstalled(Guid id) => Installed.ContainsKey(id);

    public string Of(Guid id, string fallback) =>
        Installed.TryGetValue(id, out string? name) && name.Trim().Length > 0 ? name : fallback;
}
#endif
