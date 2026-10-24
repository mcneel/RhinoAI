using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace sdk.tests;

internal sealed class TestDefinition : IAgentDefinition
{
    
    static TestDefinition()
    {
        // TODO : Use Definitions.json
        Instances.Agents.Add(new TestDefinition());

        // Deep Seek
        ModelDefinition deepSeekModel = new ("DeepSeek", "Flash", "deepseek-flash");

        HttpAgentDefinition deepSeekMessages = new ("DeepSeek", "Deep Seek (Messages)") { DefaultModelId = "deepseek-flash", Protocol = ProtocolKind.Messages, AvailableModels = { deepSeekModel } };
        HttpAgentDefinition deepSeekCompletions = new ("DeepSeek", "Deep Seek (Completions)") { DefaultModelId = "deepseek-flash", Protocol = ProtocolKind.Completions, AvailableModels = { deepSeekModel } };
        HttpAgentDefinition deepSeekResponses = new ("DeepSeek", "Deep Seek (Responses)") { DefaultModelId = "deepseek-flash", Protocol = ProtocolKind.Responses, AvailableModels = { deepSeekModel } };
        
        Instances.Agents.Add(deepSeekMessages);
        Instances.Agents.Add(deepSeekCompletions);
        Instances.Agents.Add(deepSeekResponses);

        // Claude
        ModelDefinition opusModel = new ("Anthropic", "Opus", "opus");
        
        CliAgentDefinition claudeDesktop = new ("Anthropic", "Claude (CLI)") { DefaultModelId = "opus", AvailableModels = { opusModel } };
        Instances.Agents.Add(claudeDesktop);
        
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Vendor { get; set; } = "RhinoAI";

    public string DisplayName { get; set; } = "Test Agent";

    public string DefaultPrompt { get; set; } = "";

    public string SvgIcon { get; set; } = "";

    public string DefaultModelId { get; set; } = "";

    public string? CurrentModelId { get; set; }

    public List<ModelDefinition> PrivateModelDefinitions { get; } = [];
    public IReadOnlyList<ModelDefinition> AvailableModels => PrivateModelDefinitions;

    public bool Available { get; set; } = true;

    public List<WeakReference<Agent>> PrivateRegisteredAgents { get; } = [];
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

    public IModel? CurrentModel=> GetModel(CurrentModelId ?? DefaultModelId ?? "");

    public Agent? GetAgent(PlugInToken token)
        => new(this, token, new GenericHarness(token));

    public IModel? GetModel(string modelName) => new TestModel() { Name = modelName };

    public Agent? WithModel(PlugInToken token, string modelName)
    {
        throw new NotImplementedException();
    }

    public sealed class TestModel : IModel
    {
        public string Name { get; set; } = "Test";

        public string Vendor { get; set; } = "RhinoAI";

        public bool Available { get; set; } = true;

        public Func<IHarness, IEnumerable<ITurn>, CancellationToken, Task<IEnumerable<ITurn>>>? SendAsyncFunc { get; set; }
        public Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
            => SendAsyncFunc!.Invoke(harness, turns, token);

        public Func<IHarness, IEnumerable<ITurn>, CancellationToken, IAsyncEnumerable<ITurn>>? StreamAsyncFunc { get; set; }

        public IAsyncEnumerable<ITurn> StreamAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token)
            => StreamAsyncFunc!.Invoke(harness, turn, token);

    }

}
