using System.IO;

using Rhino.AI.Paths;

namespace Rhino.AI.UI.Settings;

internal sealed class SearchPathItemViewModel : SimpleViewModel
{
    private string text;

    public SearchPathItemViewModel(string text)
    {
        this.text = text;
        Found = Resolve(text);
    }

    public string Text
    {
        get => text;
        set
        {
            if (text == value)
                return;

            // Listeners read Match as soon as Text is raised, so the stale match must be gone first.
            Found = Resolve(value);
            SetProperty(ref text, value);
        }
    }

    public string? Match => Found.Value;

    private Lazy<string?> Found { get; set; }

    private static Lazy<string?> Resolve(string pattern) => new(() =>
    {
        try
        {
            return new Glob(pattern.Trim(), IncludeDirs: false).TruePaths.FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    });
}
