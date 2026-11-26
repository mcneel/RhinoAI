using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class AgentItemViewModel : SimpleViewModel
{
    private string name;
    private bool enabled;
    private string prompt;
    private ModelItemViewModel? defaultModel;
    private bool isDefault;
    private IReadOnlyList<string> problems = [];
    private ModelsNote? modelsNote;

    public AgentItemViewModel(AgentSettings settings, bool isNew, AgentsContext context)
    {
        Id = settings.Id;
        Vendor = settings.Vendor;
        IsBuiltin = settings.Builtin;
        IsNew = isNew;
        Platforms = settings.Platforms;
        Context = context;
        name = settings.Name;
        enabled = settings.Enabled;
        prompt = settings.Prompt;
        Models = new ObservableCollection<ModelItemViewModel>(settings.Models.Select(model => Watch(new ModelItemViewModel(model.Id, model.Name))));
        defaultModel = settings.DefaultModelId is string wanted ? Models.FirstOrDefault(model => AgentNames.IsSame(model.Id, wanted)) : null;
        Connection = settings.Connection switch
        {
            CliConnection cli => new CliConnectionViewModel(context.Os, cli.WindowsPaths, cli.MacPaths),
            ApiConnection api => new ApiConnectionViewModel(api.Host, api.Protocol, api.KeyName, api.KeySource, context, () => name),
            LocalConnection local => new LocalConnectionViewModel(local.Host, local.Protocol, local.ModelsRoute),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Connection, null),
        };

        Models.CollectionChanged += (_, _) => ModelsChanged();
        Connection.PropertyChanged += (_, _) =>
        {
            RaiseStatus();
            Edited?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler<EventArgs>? Edited;

    public string Id { get; }

    public string Vendor { get; }

    public bool IsBuiltin { get; }

    public AgentConnectionViewModel Connection { get; }

    public ObservableCollection<ModelItemViewModel> Models { get; }

    private bool IsNew { get; }

    private IReadOnlyList<HostOs> Platforms { get; }

    private AgentsContext Context { get; }

    public string Name
    {
        get => name;
        set
        {
            string previous = name;
            if (!SetProperty(ref name, value))
                return;

            if (IsNew && Connection is ApiConnectionViewModel api)
                api.FollowName(previous, value);
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(Text));
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public string DisplayName => name.Trim().Length > 0 ? name.Trim() : LOC.STR("Untitled agent");

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (!SetProperty(ref enabled, value))
                return;

            RaisePropertyChanged(nameof(StarToolTip));
            RaiseStatus();
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Prompt
    {
        get => prompt;
        set
        {
            if (SetProperty(ref prompt, value))
                Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsDefault
    {
        get => isDefault;
        set
        {
            if (SetProperty(ref isDefault, value))
                RaisePropertyChanged(nameof(StarToolTip));
        }
    }

    public string? StarToolTip => (isDefault, enabled) switch
    {
        (true, true) => LOC.STR("Default agent: the AI panel starts with it."),
        (true, false) => LOC.STR("Default agent, but disabled. Enable it so the AI panel starts with it."),
        (false, true) => LOC.STR("Make this the agent the AI panel starts with."),
        (false, false) => null,
    };

    public string RemoveToolTip => IsBuiltin
        ? LOC.STR("Removed when you click OK. You can add it back from Add Agent.")
        : LOC.STR("Removed when you click OK.");

    public string Subtitle => Vendor.Trim();

    public IReadOnlyList<ModelItemViewModel> NamedModels => Models.Where(model => model.IsNamed).ToList();

    public bool HasNamedModels => Models.Any(model => model.IsNamed);

    public ModelItemViewModel? EffectiveModel
    {
        get => defaultModel is { IsNamed: true } chosen && Models.Contains(chosen) ? chosen : Models.FirstOrDefault(model => model.IsNamed);
        set
        {
            if (ReferenceEquals(value, EffectiveModel))
                return;

            defaultModel = value;
            RaiseModels();
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    public string NoModelsHelp => Connection switch
    {
        CliConnectionViewModel => LOC.STR("Add the models this app can use."),
        ApiConnectionViewModel => LOC.STR("Add the model IDs this endpoint offers."),
        LocalConnectionViewModel => LOC.STR("Load the models from the server, or add one by hand."),
        _ => throw new InvalidOperationException("Unknown connection."),
    };

    public bool CanLoadModels => Connection is LocalConnectionViewModel local && HttpAddress.IsValid(local.Host);

    public bool IsLocal => Connection is LocalConnectionViewModel;

    public string ModelsNoteText => modelsNote?.Text ?? string.Empty;

    public bool HasModelsNote => modelsNote is not null;

    public bool IsModelsNoteError => modelsNote is { IsError: true };

    public AgentStatus Status
    {
        get
        {
            if (!enabled)
                return new AgentStatus(AgentStatusKind.Disabled, LOC.STR("This agent is disabled, so the AI panel cannot use it."));
            if (Platforms.Count > 0 && !Platforms.Contains(Context.Os))
                return Context.Os == HostOs.Windows
                    ? new AgentStatus(AgentStatusKind.MacOnly, LOC.STR("This agent only runs on a Mac."))
                    : new AgentStatus(AgentStatusKind.WindowsOnly, LOC.STR("This agent only runs on Windows."));
            if (!Context.InternetAllowed)
                return new AgentStatus(AgentStatusKind.Offline, LOC.STR("Internet access is off in Rhino, so agents cannot connect."));

            AgentStatus check = Connection.Check;
            if (check.Kind != AgentStatusKind.Ready)
                return check;

            return HasNamedModels ? check : new AgentStatus(AgentStatusKind.NeedsModel, NoModelsHelp);
        }
    }

    public string ConnectionText => Connection.Check.Why;

    public StatusTone ConnectionTone => AgentStatuses.Tone(Connection.Check.Kind);

    public RowText Text
    {
        get
        {
            AgentStatusKind kind = Status.Kind;
            RowBadge? badge = kind == AgentStatusKind.Ready ? null : new RowBadge(AgentStatuses.Label(kind), AgentStatuses.Colour(AgentStatuses.Tone(kind)));
            string detail = EffectiveModel?.Label ?? LOC.STR("No model");
            return new RowText(DisplayName, detail, true, badge, null, !enabled);
        }
    }

    public string ToolTip => Status.Why;

    public string ProblemText => string.Join(" ", problems);

    public bool HasProblems => problems.Count > 0;

    public ModelItemViewModel AddModel()
    {
        ModelItemViewModel model = Watch(new ModelItemViewModel(string.Empty, string.Empty));
        Models.Add(model);
        return model;
    }

    public void RemoveModel(ModelItemViewModel model) => Models.Remove(model);

    public Task<ModelsResult> FetchModelsAsync(CancellationToken cancellation) => Connection is LocalConnectionViewModel local
        ? ModelsFetcher.FetchAsync(local.Host.Trim(), local.ModelsRoute.Trim(), cancellation)
        : throw new InvalidOperationException("Only local servers list their models.");

    public void UseFetched(ModelsResult result)
    {
        modelsNote = result switch
        {
            ModelsFound found => new ModelsNote(Merge(found.Models) switch
            {
                0 => LOC.STR("Every model on the server is already listed."),
                1 => LOC.STR("Added 1 model from the server."),
                int added => string.Format(LOC.STR("Added {0} models from the server."), added),
            }, false),
            ModelsFailed failed => new ModelsNote(failed.Message, true),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
        RaisePropertyChanged(nameof(ModelsNoteText));
        RaisePropertyChanged(nameof(HasModelsNote));
        RaisePropertyChanged(nameof(IsModelsNoteError));
    }

    public void ShowProblems(IReadOnlyList<string> messages)
    {
        problems = messages;
        RaisePropertyChanged(nameof(ProblemText));
        RaisePropertyChanged(nameof(HasProblems));
    }

    public AgentDraft ToDraft() => new(
        Id,
        name,
        Vendor,
        enabled,
        EffectiveModel?.Id.Trim(),
        Models.Select(model => new ModelEntry(model.Id, model.Name)).ToList(),
        prompt,
        Connection.ToDraft(),
        Platforms);

    public AgentSettings ToSettings(string id, string newName) => new(
        id,
        newName,
        Vendor,
        enabled,
        EffectiveModel?.Id.Trim(),
        Models.Select(model => new ModelEntry(model.Id, model.Name)).ToList(),
        prompt,
        Connection.ToSettings(),
        false,
        Platforms);

    private int Merge(IReadOnlyList<ModelEntry> fetched)
    {
        HashSet<string> known = new(Models.Select(model => model.Id.Trim()), StringComparer.Ordinal);
        int added = 0;
        foreach (ModelEntry entry in fetched)
        {
            string id = entry.Id.Trim();
            if (id.Length == 0 || !known.Add(id))
                continue;

            Models.Add(Watch(new ModelItemViewModel(id, entry.Name.Trim().Length > 0 ? entry.Name.Trim() : id)));
            added++;
        }

        return added;
    }

    private ModelItemViewModel Watch(ModelItemViewModel model)
    {
        model.PropertyChanged += (_, _) => ModelsChanged();
        return model;
    }

    private void ModelsChanged()
    {
        RaiseModels();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseModels()
    {
        foreach (string property in new[] { nameof(NamedModels), nameof(HasNamedModels), nameof(EffectiveModel) })
            RaisePropertyChanged(property);
        RaiseStatus();
    }

    private void RaiseStatus()
    {
        foreach (string property in new[] { nameof(Status), nameof(ConnectionText), nameof(ConnectionTone), nameof(CanLoadModels), nameof(Text), nameof(ToolTip) })
            RaisePropertyChanged(property);
    }

    private sealed record ModelsNote(string Text, bool IsError);
}
