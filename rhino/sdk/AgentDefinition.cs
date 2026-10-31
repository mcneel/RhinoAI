using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Rhino.AI.Models;
using System.Text.Json.Serialization;

namespace Rhino.AI;

// NOTE : This, as a contract, feels correct. 
//        A User will want to create and modify definitions. Maybe a user wants TWO Claude API subscriptions.
//        Maybe the only difference is the default prompt
//        Maybe the user has a custom AI Endpoint! Local or Remote! A domain I don't know of, and all the necessary ingredients

/// <summary>
/// A Definition of an Agent, all the information required to correctly construct it.
/// </summary>
public interface IAgentDefinition
{

    public Guid Id { get; }

    public string Vendor { get; }

    public string DisplayName { get; }

    public string DefaultPrompt { get; }

    public string SvgIcon { get; }

    // Search Path is valid OR API Key exists
    public bool Available { get; }

    public string DefaultModelId { get; set; }

    // Looks like a dev could currently change this?
    public string? CurrentModelId { get; internal set; }

    internal IModel? CurrentModel { get; }

    public PermissionSet Permissions { get; }

    public bool Enabled { get; set; }

    public IReadOnlyCollection<ModelDefinition> AvailableModels { get; }

    internal IEnumerable<Agent> RegisteredAgents { get; }

    internal void RegisterAgent(Agent agent);

    internal IModel? GetModel(string modelName);

    public Agent? GetAgent(PlugIns.PlugInToken token);

    internal Agent? WithModel(PlugIns.PlugInToken token, string modelName);

}

internal abstract class AgentDefinition(string vendor, string displayName) : IAgentDefinition
{

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Vendor { get; } = vendor;

    public string DisplayName { get; } = displayName;

    public string DefaultPrompt { get; set; } = "";

    public string SvgIcon { get; set; } = "";

    public string DefaultModelId { get; set; } = "";

    public string? CurrentModelId { get; set; }

    public bool Enabled { get; set; } = true;

    // TODO : Document WHY this exists, I've forgotten
    public Platform Platforms { get; init; } = Platform.All;

    public PermissionSet Permissions { get; } = new();

    protected HashSet<ModelDefinition> PrivateAvailableModels { get; init; } = [];
    public IReadOnlyCollection<ModelDefinition> AvailableModels => PrivateAvailableModels;

    public bool Add(ModelDefinition definition) => PrivateAvailableModels.Add(definition);

    public bool Add(IModel definition)
    {
        // TODO : IModel.Id?
        if (!PrivateAvailableModels.Add(new ModelDefinition(definition.Vendor, definition.Name, definition.Name))) return false;
        if (string.IsNullOrEmpty(CurrentModelId)) DefaultModelId = definition.Name;
        return true;
    }

    public abstract bool Available { get; }

    protected List<WeakReference<Agent>> PrivateRegisteredAgents { get; } = [];
    public IEnumerable<Agent> RegisteredAgents
    {
        get
        {
            foreach (WeakReference<Agent> agentRef in PrivateRegisteredAgents)
            {
                if (!agentRef.TryGetTarget(out Agent? agent)) continue;
                if (agent is null) continue;
                yield return agent;
            }
        }
    }

    public void RegisterAgent(Agent agent)
    {
        PrivateRegisteredAgents.Add(new(agent));
    }

    public IModel? CurrentModel => GetModel(CurrentModelId ?? DefaultModelId ?? "");

    public abstract IModel? GetModel(string modelName);

    public abstract Agent? WithModel(PlugIns.PlugInToken token, string modelName);

    public abstract Agent? GetAgent(PlugIns.PlugInToken token);

}

internal sealed class CliAgentDefinition(string vendor, string displayName) : AgentDefinition(vendor, displayName)
{

    public List<string> WinSearchPaths { get; init; } = [];
    public List<string> MacSearchPaths { get; init; } = [];

    public override bool Available
    {
        get
        {
            IEnumerable<string> paths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WinSearchPaths : MacSearchPaths;
            foreach (string path in paths)
            {
                // TODO : Glob
                if (System.IO.File.Exists(path)) return true;
            }

            return false;
        }
    }

    public override IModel? GetModel(string modelName)
    {
        ModelDefinition? def = PrivateAvailableModels.FirstOrDefault(pd => string.Equals(pd.ModelId, modelName, StringComparison.OrdinalIgnoreCase));
        if (def is null) return null;

        IEnumerable<string> searchPaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WinSearchPaths : MacSearchPaths;

        IModel? model = Vendor.ToLowerInvariant() switch
        {
            "anthropic" => new ClaudeDesktopModel(def.ModelId, searchPaths),
            "openai" => new CodexDesktopModel(def.ModelId, searchPaths),
            // "google" => (n) => new GeminiDesktopModel(n),
            // "deepseek" => (n) => new DeepSeekDesktopModel(n),

            _ => null
        };

        return model;
    }

    public override Agent? WithModel(PlugIns.PlugInToken token, string modelName)
    {
        AgentDefinition definition = new CliAgentDefinition(Vendor, DisplayName)
        {
            CurrentModelId = modelName,
            WinSearchPaths = this.WinSearchPaths,
            MacSearchPaths = this.MacSearchPaths,
            Platforms = this.Platforms,
        };

        return definition.GetAgent(token);
    }

    public override Agent? GetAgent(PlugIns.PlugInToken token)
    {
        IModel? model = CurrentModel;
        if (model is null) return null;

        Harness? harness = Vendor.ToLowerInvariant() switch
        {
            "anthropic" => new ClaudeHarness(token),
            "openai" => new CodexHarness(token),
            // "google" => (n) => new GeminiHarness(n),
            // "deepseek" => (n) => new DeepSeekHarness(n),

            _ => null
        };

        if (harness is null) return null;

        return new Agent(this, token, harness);
    }

}

internal sealed class HttpAgentDefinition(string vendor, string displayName) : AgentDefinition(vendor, displayName)
{

    public override bool Available => ModelsRoute is not null || !string.IsNullOrEmpty(ApiKey);

    public string? Host { get; init; }

    public ProtocolKind Protocol { get; init; } = ProtocolKind.Default;

    // Local only
    public string? ModelsRoute { get; init; }

    [JsonPropertyName("envVar")]
    public string? ApiKeyEnvVar { get; init; }

    private string? PrivateApiKey { get; set; }
    
    [JsonPropertyName("apiKey")]
    public string? ApiKey
    {
        get => PrivateApiKey ??=
            (Environment.GetEnvironmentVariable(ApiKeyEnvVar ?? string.Empty)
            ?? Secrets.Stasher.GetSecret(ApiKeyEnvVar ?? string.Empty));
        set
        {
            if (string.IsNullOrEmpty(ApiKeyEnvVar)) return;

            if (string.IsNullOrEmpty(value))
            {
                Secrets.Stasher.TryDeleteSecret(ApiKeyEnvVar);
            }
            else
            {
                Secrets.Stasher.TrySetSecret(ApiKeyEnvVar, value);
            }
        }
    }

    public override Agent? WithModel(PlugIns.PlugInToken token, string modelName)
    {
        AgentDefinition definition = new HttpAgentDefinition(Vendor, DisplayName)
        {
            Host = this.Host,
            Protocol = this.Protocol,
            ModelsRoute = this.ModelsRoute,
            ApiKeyEnvVar = this.ApiKeyEnvVar,
            PrivateApiKey = this.PrivateApiKey,
            Platforms = this.Platforms,
            CurrentModelId = modelName
        };

        return definition.GetAgent(token);
    }

    public override IModel? GetModel(string modelName)
    {
        ModelDefinition? def = PrivateAvailableModels.FirstOrDefault(pd => string.Equals(pd.ModelId, modelName, StringComparison.OrdinalIgnoreCase));
        if (def is null) return null;

        Func<string, IModel>? modelFactory = Vendor.ToLowerInvariant() switch
        {
            "anthropic" => Protocols(ClaudeModel.Default, ClaudeModel.Messages, ClaudeModel.Completions),
            "openai" => Protocols(ChatGptModel.Default, null, ChatGptModel.Completions, ChatGptModel.Responses),
            "google" => Protocols(GeminiModel.Default, null, GeminiModel.Completions, null),
            "deepseek" => Protocols(DeepSeekModel.Default, DeepSeekModel.Anthropic, DeepSeekModel.Completions, DeepSeekModel.Responses),
            "lm studio" => Protocols(LocalModel.Default, LocalModel.Anthropic, LocalModel.Completions, LocalModel.Responses),
            // TODO : Fix
            // "Ollama" => Protocols(null),
            "apple" => Protocols(LocalModel.Default, null, LocalModel.Completions, null),

            _ => Protocols(null, UnknownModel.Anthropic, UnknownModel.Completions, UnknownModel.Completions),
        };

        if (modelFactory is null) return null;

        IModel model = modelFactory.Invoke(def.ModelId);

        // Ensure the API Key is injected
        if (model is ApiModel apiModel)
        {
            apiModel.ApiKey = ApiKey;
        }

        return model;
    }

    private Func<string, IModel>? Protocols(Func<string, IModel> @default,
                                            Func<string, IModel>? messages = null,
                                            Func<string, IModel>? completions = null,
                                            Func<string, IModel>? responses = null)
        => Protocol switch
        {
            ProtocolKind.Default => @default,
            ProtocolKind.Messages => messages,
            ProtocolKind.Completions => completions,
            ProtocolKind.Responses => responses,

            _ => null
        };

    private Func<string, IModel>? Protocols(Func<string, Uri, IModel> @default,
                                            Func<string, Uri, IModel>? messages = null,
                                            Func<string, Uri, IModel>? completions = null,
                                            Func<string, Uri, IModel>? responses = null)
        => Protocol switch
        {
            _ when Host is null => null,

            ProtocolKind.Default when @default is not null => (m) => @default(m, new Uri(Host)),
            ProtocolKind.Messages when messages is not null => (m) => messages(m, new Uri(Host)),
            ProtocolKind.Completions when completions is not null => (m) => completions(m, new Uri(Host)),
            ProtocolKind.Responses when responses is not null => (m) => responses(m, new Uri(Host)),

            _ => null
        };

    private Func<string, IModel>? Protocols(Func<string, string, Uri, IModel>? @default,
                                            Func<string, string, Uri, IModel>? messages = null,
                                            Func<string, string, Uri, IModel>? completions = null,
                                            Func<string, string, Uri, IModel>? responses = null)
        => Protocol switch
        {
            _ when Host is null => null,

            ProtocolKind.Default when @default is not null => (m) => @default(m, Vendor, new Uri(Host)),
            ProtocolKind.Messages when messages is not null => (m) => messages(m, Vendor, new Uri(Host)),
            ProtocolKind.Completions when completions is not null => (m) => completions(m, Vendor, new Uri(Host)),
            ProtocolKind.Responses when responses is not null => (m) => responses(m, Vendor, new Uri(Host)),

            _ => null
        };

    public override Agent? GetAgent(PlugIns.PlugInToken token)
    {
        IModel? model = GetModel(CurrentModelId ?? DefaultModelId ?? "") ?? CurrentModel;
        if (model is null) return null;

        return new Agent(this, token, new Harness(token));
    }

}

[Flags]
internal enum Platform { None = 0, Windows = 1, OSX = 2, All = Windows | OSX }

public sealed record ModelDefinition(string Vendor, string DisplayName, string ModelId);
