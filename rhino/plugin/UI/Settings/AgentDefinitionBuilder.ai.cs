using Rhino.UI;

using Rhino.AI.Secrets;

namespace Rhino.AI.UI.Settings;

internal static class AgentDefinitionBuilder
{
    public static AgentDefinition Build(AgentDraft draft, Guid id, IAgentDefinition? previous)
    {
        string vendor = draft.Vendor.Trim();
        string name = draft.Name.Trim();
        Platform platforms = PlatformOf(draft.Platforms);
        IReadOnlyList<ModelDefinition> models = ModelsOf(draft.Models, vendor);

        AgentDefinition definition = draft.Connection switch
        {
            CliConnectionDraft cli => new CliAgentDefinition(vendor, name)
            {
                Id = id,
                Platforms = platforms,
                WinSearchPaths = NonBlank(cli.WindowsPaths),
                MacSearchPaths = NonBlank(cli.MacPaths),
            },
            ApiConnectionDraft api => new HttpAgentDefinition(vendor, name)
            {
                Id = id,
                Platforms = platforms,
                Host = api.Host.Trim(),
                Protocol = api.Protocol,
                ApiKeyEnvVar = BlankToNull(api.KeyName),
            },
            LocalConnectionDraft local => new HttpAgentDefinition(vendor, name)
            {
                Id = id,
                Platforms = platforms,
                Host = local.Host.Trim(),
                Protocol = local.Protocol,
                ModelsRoute = local.ModelsRoute.Trim(),
                ApiKeyEnvVar = (previous as HttpAgentDefinition)?.ApiKeyEnvVar,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(draft), draft.Connection, null),
        };

        definition.DefaultPrompt = draft.Prompt;
        definition.SvgIcon = previous?.SvgIcon ?? string.Empty;
        definition.Enabled = draft.Enabled;
        foreach (ModelDefinition model in models)
            definition.Add(model);

        definition.DefaultModelId = BlankToNull(draft.DefaultModelId) ?? models.FirstOrDefault()?.ModelId ?? string.Empty;
        // A kept current model that is no longer listed would leave the agent unable to start, so it falls back to the default.
        definition.CurrentModelId = previous?.CurrentModelId is string current && models.Any(m => string.Equals(m.ModelId, current, StringComparison.OrdinalIgnoreCase))
            ? current
            : null;

        if (previous is not null)
        {
            definition.Permissions.DefaultPermission = previous.Permissions.DefaultPermission;
            foreach (Permission permission in previous.Permissions.All)
                definition.Permissions.AddPermission(permission);
        }

        return definition;
    }

    public static SettingsProblem? ApplyKey(HttpAgentDefinition definition, AgentDraft draft, KeyChange change) => change switch
    {
        KeepKey => null,
        SetKey set => Store(definition, draft, set.Value.Trim()),
        ClearKey => Forget(definition, draft),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    private static SettingsProblem? Store(HttpAgentDefinition definition, AgentDraft draft, string value)
    {
        if (definition.ApiKeyEnvVar is not string keyName)
            return KeyProblem(draft, "keyName", LOC.STR("Enter a key name so Rhino can save this API key."));

        definition.ApiKey = value;
        return Stasher.TryGetSecret(keyName, out string? stored) && string.Equals(stored, value, StringComparison.Ordinal)
            ? null
            : KeyProblem(draft, "key", string.Format(LOC.STR("Rhino could not save the API key for {0}."), draft.Name.Trim()));
    }

    private static SettingsProblem? Forget(HttpAgentDefinition definition, AgentDraft draft)
    {
        if (definition.ApiKeyEnvVar is not string keyName)
            return null;

        definition.ApiKey = null;
        return Stasher.TryGetSecret(keyName, out _)
            ? KeyProblem(draft, "key", string.Format(LOC.STR("Rhino could not remove the API key for {0}."), draft.Name.Trim()))
            : null;
    }

    private static SettingsProblem KeyProblem(AgentDraft draft, string field, string message) =>
        new(draft.Id, field, message);

    private static IReadOnlyList<ModelDefinition> ModelsOf(IReadOnlyList<ModelEntry> entries, string vendor)
    {
        List<ModelDefinition> models = new(entries.Count);
        foreach (ModelEntry entry in entries)
        {
            string modelId = entry.Id.Trim();
            string displayName = entry.Name.Trim();
            if (modelId.Length == 0)
                continue;

            if (!OutputTokenLimit.TryParse(entry.MaxOutputTokens, out int? maxOutputTokens))
                throw new ArgumentException(string.Format("The max output tokens of {0} were not validated before building.", modelId), nameof(entries));

            models.Add(new ModelDefinition(vendor, displayName.Length > 0 ? displayName : modelId, modelId) { MaxOutputTokens = maxOutputTokens });
        }
        return models;
    }

    private static Platform PlatformOf(IReadOnlyList<HostOs> platforms)
    {
        Platform result = Platform.None;
        foreach (HostOs os in platforms)
        {
            result |= os switch
            {
                HostOs.Windows => Platform.Windows,
                HostOs.Mac => Platform.OSX,
                _ => throw new ArgumentOutOfRangeException(nameof(platforms), os, null),
            };
        }
        return result;
    }

    private static List<SearchPath> NonBlank(IReadOnlyList<SearchPath> paths) =>
        [.. paths.Select(p => p with { Path = p.Path.Trim() }).Where(p => p.Path.Length > 0)];

    private static string? BlankToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
