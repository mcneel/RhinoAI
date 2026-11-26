#if R9
using System.Collections.ObjectModel;

using Rhino.Runtime;
using Rhino.UI;

using Rhino.AI.Models;
using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed record AgentTemplateGroup(AgentKind Kind, string Label, IReadOnlyList<AgentSettings> Templates);

internal sealed class AgentsPageViewModel : SimpleViewModel
{
    private const string CustomVendor = "Custom";
    private const string DefaultModelsRoute = "/v1/models";

    private AgentItemViewModel? selected;
    private AgentItemViewModel? defaultAgent;
    private bool isChanged;
    private string pageProblem = string.Empty;
    private bool isModelExpanded;
    private bool isConnectionExpanded;
    private bool isPromptExpanded;

    private AgentsPageViewModel(IReadOnlyList<AgentSettings> agents, string? defaultAgentId, IReadOnlyList<AgentSettings> templates, AgentsContext context)
    {
        Context = context;
        Templates = templates;
        Agents = new ObservableCollection<AgentItemViewModel>(agents.Select(agent => Watch(new AgentItemViewModel(agent, false, context))));
        defaultAgent = Agents.FirstOrDefault(agent => agent.Id == defaultAgentId);
        if (defaultAgent is not null)
            defaultAgent.IsDefault = true;
        selected = defaultAgent ?? Agents.FirstOrDefault();
        Agents.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(IsEmpty));
    }

    public static AgentsPageViewModel Load()
    {
        IReadOnlyList<AgentDefinition> builtin = AgentSettingsReader.BuiltinDefinitions();
        IReadOnlyList<AgentSettings> agents = AgentSettingsReader.Current(builtin);
        string preferred = AIInstances.Preferences.PreferedAgentId.ToString();
        Dictionary<string, KeySource> sources = new(StringComparer.Ordinal);
        KeySource SourceOf(string? keyName)
        {
            if (keyName is not string name || name.Trim().Length == 0)
                return KeySource.None;
            if (!sources.TryGetValue(name, out KeySource source))
                sources[name] = source = AgentSettingsReader.KeySourceOf(name);
            return source;
        }

        AgentsContext context = new(HostUtils.RunningOnOSX ? HostOs.Mac : HostOs.Windows, RhinoApp.IsInternetAccessAllowed, SourceOf);
        return new AgentsPageViewModel(agents, agents.Any(agent => agent.Id == preferred) ? preferred : null, AgentSettingsReader.Templates(builtin), context);
    }

    public ObservableCollection<AgentItemViewModel> Agents { get; }

    public bool IsEmpty => Agents.Count == 0;

    public bool IsOffline => !Context.InternetAllowed;

    public string PageProblem => pageProblem;

    public bool HasPageProblem => pageProblem.Length > 0;

    public AgentItemViewModel? Selected
    {
        get => selected;
        set => SetProperty(ref selected, value);
    }

    public IReadOnlyList<AgentTemplateGroup> TemplateGroups => new[] { AgentKind.Cli, AgentKind.Api, AgentKind.Local }
        .Select(kind => new AgentTemplateGroup(kind, GroupLabel(kind), Templates.Where(template => KindOf(template) == kind && RunsHere(template)).ToList()))
        .Where(group => group.Templates.Count > 0)
        .ToList();

    public bool IsModelExpanded
    {
        get => isModelExpanded;
        set => SetProperty(ref isModelExpanded, value);
    }

    public bool IsConnectionExpanded
    {
        get => isConnectionExpanded;
        set => SetProperty(ref isConnectionExpanded, value);
    }

    public bool IsPromptExpanded
    {
        get => isPromptExpanded;
        set => SetProperty(ref isPromptExpanded, value);
    }

    private IReadOnlyList<AgentSettings> Templates { get; }

    private AgentsContext Context { get; }

    public void AddFromTemplate(AgentSettings template)
        => Insert(template with { Id = NewId(), Name = AgentNames.Unique(template.Name, TakenNames()), Enabled = true }, null);

    public void AddCustom(AgentKind kind)
    {
        string name = AgentNames.Unique(kind == AgentKind.Api ? LOC.STR("Custom API") : LOC.STR("Local server"), TakenNames());
        AgentConnection connection = kind switch
        {
            AgentKind.Api => new ApiConnection(string.Empty, ProtocolKind.Completions, AgentNames.KeyNameFor(name), KeySource.None),
            AgentKind.Local => new LocalConnection(string.Empty, ProtocolKind.Completions, DefaultModelsRoute),
            AgentKind.Cli => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Command-line agents come from templates."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Insert(new AgentSettings(NewId(), name, CustomVendor, true, null, [], string.Empty, connection, false, [HostOs.Windows, HostOs.Mac]), null);
    }

    public void Duplicate(AgentItemViewModel agent)
        => Insert(agent.ToSettings(NewId(), AgentNames.Unique(string.Format(LOC.STR("{0} copy"), agent.DisplayName), TakenNames())), agent);

    public void Remove(AgentItemViewModel agent)
    {
        int index = Agents.IndexOf(agent);
        if (index < 0)
            return;

        Agents.RemoveAt(index);
        if (ReferenceEquals(defaultAgent, agent))
            MakeDefault(null);
        Selected = Agents.Count == 0 ? null : Agents[Math.Min(index, Agents.Count - 1)];
        isChanged = true;
    }

    public void MakeDefault(AgentItemViewModel? agent)
    {
        if (ReferenceEquals(defaultAgent, agent))
            return;
        if (agent is { Enabled: false })
            throw new InvalidOperationException("A disabled agent cannot be the default.");

        if (defaultAgent is not null)
            defaultAgent.IsDefault = false;
        defaultAgent = agent;
        if (agent is not null)
            agent.IsDefault = true;
        isChanged = true;
    }

    public bool TryCommit()
    {
        if (!isChanged)
            return true;

        List<AgentDraft> drafts = Agents.Select(agent => agent.ToDraft()).ToList();
        string? defaultId = defaultAgent?.Id;
        List<SettingsProblem> problems = [.. SettingsValidator.Problems(drafts, defaultId)];
        if (problems.Count == 0)
            problems = [.. Apply(drafts, defaultId)];

        Show(problems);
        return problems.Count == 0;
    }

    private static IReadOnlyList<SettingsProblem> Apply(IReadOnlyList<AgentDraft> drafts, string? defaultId)
    {
        Dictionary<Guid, IAgentDefinition> existing = AIInstances.Agents.Definitions.ToDictionary(definition => definition.Id);
        List<AgentDefinition> built = new(drafts.Count);
        List<SettingsProblem> problems = [];
        foreach (AgentDraft agent in drafts)
        {
            Guid id = Guid.Parse(agent.Id);
            existing.TryGetValue(id, out IAgentDefinition? previous);
            AgentDefinition definition = AgentDefinitionBuilder.Build(agent, id, previous);
            if (definition is HttpAgentDefinition http
                && agent.Connection is ApiConnectionDraft api
                && AgentDefinitionBuilder.ApplyKey(http, agent, api.Key) is SettingsProblem problem)
                problems.Add(problem);
            built.Add(definition);
        }

        if (problems.Count > 0)
            return problems;

        HashSet<Guid> kept = new(built.Select(definition => definition.Id));
        foreach (IAgentDefinition stale in existing.Values.Where(definition => definition is CliAgentDefinition or HttpAgentDefinition && !kept.Contains(definition.Id)))
            AIInstances.Agents.Remove(stale.Id);

        foreach (AgentDefinition definition in built)
        {
            AIInstances.Agents.Remove(definition.Id);
            AIInstances.Agents.Add(definition);
        }

        AIInstances.Preferences.PreferedAgentId = defaultId is string chosen ? Guid.Parse(chosen) : Guid.Empty;
        return [];
    }

    private static AgentKind KindOf(AgentSettings template) => template.Connection switch
    {
        CliConnection => AgentKind.Cli,
        ApiConnection => AgentKind.Api,
        LocalConnection => AgentKind.Local,
        _ => throw new ArgumentOutOfRangeException(nameof(template), template.Connection, null),
    };

    private static string GroupLabel(AgentKind kind) => kind switch
    {
        AgentKind.Cli => LOC.STR("Command-Line Apps"),
        AgentKind.Api => LOC.STR("Cloud APIs"),
        AgentKind.Local => LOC.STR("Local Servers"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string NewId() => Guid.NewGuid().ToString();

    private bool RunsHere(AgentSettings template) => template.Platforms.Count == 0 || template.Platforms.Contains(Context.Os);

    private IEnumerable<string> TakenNames() => Agents.Select(agent => agent.Name);

    private void Insert(AgentSettings settings, AgentItemViewModel? after)
    {
        AgentItemViewModel agent = Watch(new AgentItemViewModel(settings, true, Context));
        int index = after is null ? -1 : Agents.IndexOf(after);
        if (index < 0)
            Agents.Add(agent);
        else
            Agents.Insert(index + 1, agent);

        if (defaultAgent is null && Agents.Count == 1)
            MakeDefault(agent);
        IsConnectionExpanded = true;
        Selected = agent;
        isChanged = true;
    }

    private AgentItemViewModel Watch(AgentItemViewModel agent)
    {
        agent.Edited += (_, _) => isChanged = true;
        return agent;
    }

    private void Show(IReadOnlyList<SettingsProblem> problems)
    {
        foreach (AgentItemViewModel agent in Agents)
            agent.ShowProblems(problems.Where(problem => problem.Target == agent.Id).Select(problem => problem.Message).ToList());

        pageProblem = string.Join(" ", problems.Where(problem => problem.Target is null).Select(problem => problem.Message));
        RaisePropertyChanged(nameof(PageProblem));
        RaisePropertyChanged(nameof(HasPageProblem));

        if (Agents.FirstOrDefault(agent => agent.HasProblems) is not AgentItemViewModel first)
            return;

        IsModelExpanded = true;
        IsConnectionExpanded = true;
        Selected = first;
    }
}
#endif
