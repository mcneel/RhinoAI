namespace Rhino.AI.UI.Settings;

internal sealed class ModelItemViewModel : SimpleViewModel
{
    private string id;
    private string name;
    private string maxOutputTokens;

    public ModelItemViewModel(string id, string name, string maxOutputTokens)
    {
        this.id = id;
        this.name = name;
        this.maxOutputTokens = maxOutputTokens;
    }

    public string Id
    {
        get => id;
        set
        {
            if (SetProperty(ref id, value))
                RaisePropertyChanged(nameof(Label));
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
                RaisePropertyChanged(nameof(Label));
        }
    }

    public string MaxOutputTokens
    {
        get => maxOutputTokens;
        set => SetProperty(ref maxOutputTokens, value);
    }

    public bool IsNamed => id.Trim().Length > 0;

    public string Label => name.Trim().Length > 0 ? name.Trim() : id.Trim();
}
