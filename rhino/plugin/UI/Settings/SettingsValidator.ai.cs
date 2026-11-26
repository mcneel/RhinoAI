using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class SettingsValidator
{
    public static IReadOnlyList<SettingsProblem> Problems(IReadOnlyList<AgentDraft> agents, string? defaultAgentId)
    {
        List<SettingsProblem> problems = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<Guid> ids = [];
        foreach (AgentDraft agent in agents)
            problems.AddRange(AgentProblems(agent, names, ids));

        if (defaultAgentId is string preferred && !agents.Any(agent => agent.Id == preferred))
            problems.Add(new SettingsProblem(null, "defaultAgentId", LOC.STR("The default agent must be one of your agents.")));

        return problems;
    }

    private static List<SettingsProblem> AgentProblems(AgentDraft agent, HashSet<string> takenNames, HashSet<Guid> takenIds)
    {
        List<SettingsProblem> problems = [];
        void Add(string? field, string message) => problems.Add(new SettingsProblem(agent.Id, field, message));

        if (!Guid.TryParse(agent.Id, out Guid id))
            Add(null, LOC.STR("This agent has an invalid ID."));
        else if (!takenIds.Add(id))
            Add(null, LOC.STR("Another agent has the same ID."));

        string name = agent.Name.Trim();
        if (name.Length == 0)
            Add("name", LOC.STR("Enter a name for this agent."));
        else if (!takenNames.Add(name))
            Add("name", LOC.STR("Another agent already has this name."));

        if (agent.Vendor.Trim().Length == 0)
            Add("vendor", LOC.STR("Choose a vendor."));

        switch (agent.Connection)
        {
            case CliConnectionDraft cli:
                if (!cli.WindowsPaths.Concat(cli.MacPaths).Any(path => path.Enabled && path.Path.Trim().Length > 0))
                    Add("paths", LOC.STR("Add at least one search path."));
                break;

            case ApiConnectionDraft api:
                if (!HttpAddress.IsValid(api.Host))
                    Add("host", LOC.STR("Enter a full http or https address."));
                if (api.Key is SetKey set && set.Value.Trim().Length == 0)
                    Add("key", LOC.STR("Enter an API key."));
                if (api.Key is SetKey && string.IsNullOrWhiteSpace(api.KeyName))
                    Add("keyName", LOC.STR("Enter a key name so Rhino can save this API key."));
                break;

            case LocalConnectionDraft local:
                if (!HttpAddress.IsValid(local.Host))
                    Add("host", LOC.STR("Enter a full http or https address."));
                if (local.ModelsRoute.Trim().Length == 0)
                    Add("modelsRoute", LOC.STR("Enter the route that lists the models, such as /v1/models."));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(agent), agent.Connection, null);
        }

        List<string> modelIds = agent.Models
            .Select(model => (Id: model.Id.Trim(), Name: model.Name.Trim()))
            .Where(model => model.Id.Length > 0 || model.Name.Length > 0)
            .Select(model => model.Id)
            .ToList();

        if (modelIds.Any(modelId => modelId.Length == 0))
            Add("models", LOC.STR("Every model needs an ID."));

        HashSet<string> seen = new(StringComparer.Ordinal);
        if (modelIds.FirstOrDefault(modelId => modelId.Length > 0 && !seen.Add(modelId)) is string repeated)
            Add("models", string.Format(LOC.STR("Each model ID can only be listed once. {0} appears more than once."), repeated));

        string defaultModel = agent.DefaultModelId?.Trim() ?? string.Empty;
        if (defaultModel.Length > 0 && !modelIds.Any(modelId => string.Equals(modelId, defaultModel, StringComparison.OrdinalIgnoreCase)))
            Add("defaultModelId", LOC.STR("The default model must be one of this agent's models."));

        return problems;
    }
}
