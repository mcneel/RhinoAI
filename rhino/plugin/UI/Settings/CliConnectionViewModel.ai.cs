using System.Collections.ObjectModel;
using System.IO;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class CliConnectionViewModel : AgentConnectionViewModel
{
    public CliConnectionViewModel(HostOs os, IReadOnlyList<SearchPath> windowsPaths, IReadOnlyList<SearchPath> macPaths)
    {
        Os = os;
        OtherPaths = os == HostOs.Windows ? macPaths : windowsPaths;
        SavedPaths = os == HostOs.Windows ? windowsPaths : macPaths;
        Paths = new ObservableCollection<SearchPathItemViewModel>(SavedPaths.Where(path => path.Enabled).Select(path => Watch(new SearchPathItemViewModel(path.Path))));
        Paths.CollectionChanged += (_, _) => RaiseCheck();
    }

    public HostOs Os { get; }

    public ObservableCollection<SearchPathItemViewModel> Paths { get; }

    private IReadOnlyList<SearchPath> OtherPaths { get; }

    private IReadOnlyList<SearchPath> SavedPaths { get; }

    public string? ResolvedPath => Paths.Select(path => path.Text.Trim()).FirstOrDefault(path => path.Length > 0 && File.Exists(path));

    public bool HasUnexpandedPaths => ResolvedPath is null && Paths.Any(path => NeedsExpansion(path.Text));

    public override AgentStatus Check => ResolvedPath is string path
        ? new AgentStatus(AgentStatusKind.Ready, string.Format(LOC.STR("Found at {0}"), path))
        : new AgentStatus(AgentStatusKind.NotFound, LOC.STR("Not found on this computer. Install the app, or add the path where it is installed."));

    public SearchPathItemViewModel Add(string text)
    {
        SearchPathItemViewModel path = Watch(new SearchPathItemViewModel(text));
        Paths.Add(path);
        return path;
    }

    public void Remove(SearchPathItemViewModel path) => Paths.Remove(path);

    public override ConnectionDraft ToDraft()
    {
        IReadOnlyList<SearchPath> here = CurrentPaths();
        return Os == HostOs.Windows ? new CliConnectionDraft(here, OtherPaths) : new CliConnectionDraft(OtherPaths, here);
    }

    public override AgentConnection ToSettings()
    {
        IReadOnlyList<SearchPath> here = CurrentPaths();
        return Os == HostOs.Windows ? new CliConnection(here, OtherPaths) : new CliConnection(OtherPaths, here);
    }

    // A saved path that was removed or retyped stays as a disabled entry, so a newer Definitions.json never adds it back.
    private IReadOnlyList<SearchPath> CurrentPaths()
    {
        List<SearchPath> current = [];
        foreach (string text in Paths.Select(path => path.Text.Trim()))
        {
            if (text.Length > 0 && !current.Any(kept => IsSamePath(kept.Path, text)))
                current.Add(new SearchPath(text, true));
        }

        foreach (SearchPath saved in SavedPaths)
        {
            if (!current.Any(kept => IsSamePath(kept.Path, saved.Path)))
                current.Add(saved with { Enabled = false });
        }

        return current;
    }

    private static bool IsSamePath(string first, string second)
        => string.Equals(first.Trim().Replace('\\', '/'), second.Trim().Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool NeedsExpansion(string path)
    {
        string trimmed = path.Trim();
        return trimmed.StartsWith("~", StringComparison.Ordinal) || trimmed.IndexOfAny(['%', '*']) >= 0;
    }

    private SearchPathItemViewModel Watch(SearchPathItemViewModel path)
    {
        path.PropertyChanged += (_, _) => RaiseCheck();
        return path;
    }

    private void RaiseCheck()
    {
        RaisePropertyChanged(nameof(Check));
        RaisePropertyChanged(nameof(HasUnexpandedPaths));
    }
}
