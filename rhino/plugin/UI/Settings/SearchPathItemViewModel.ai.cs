#if R9
namespace Rhino.AI.UI.Settings;

internal sealed class SearchPathItemViewModel : SimpleViewModel
{
    private string text;

    public SearchPathItemViewModel(string text)
    {
        this.text = text;
    }

    public string Text
    {
        get => text;
        set => SetProperty(ref text, value);
    }
}
#endif
