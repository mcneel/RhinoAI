using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace Rhino.AI;

/// <summary>
/// The Agent is the controller of all the AI parts.
/// </summary>
public sealed class Agent(PlugInToken token, IModel model, IHarness harness, string defaultPrompt = "")
{

    private PlugInToken Token { get; } = token;

    private List<ITurn> PrivateTurns { get; } = [];

    /// <summary>
    /// All turns in the conversation thus far
    /// </summary>
    public IReadOnlyList<ITurn> Turns => PrivateTurns;

    /// <summary>
    /// The Model in use by the agent
    /// </summary>
    public IModel Model { get; } = model;

    private static Dictionary<string, IModel> PrivateModelMakers { get; } = new(StringComparer.OrdinalIgnoreCase) {
        { "default", DeepSeekModel.Default() },

        { "claude-fable-5-1", ClaudeModel.Default("claude-fable-5-1") },
        { "claude-fable-5", ClaudeModel.Default("claude-fable-5") },
        { "claude-mythos-5-1", ClaudeModel.Default("claude-mythos-5-1") },
        { "claude-mythos-5", ClaudeModel.Default("claude-mythos-5") },
        { "claude-opus-5", ClaudeModel.Default("claude-opus-5") },
        { "claude-opus-4-8", ClaudeModel.Default("claude-opus-4-8") },
        { "claude-opus-4-7", ClaudeModel.Default("claude-opus-4-7") },
        { "claude-opus-4-6", ClaudeModel.Default("claude-opus-4-6") },
        { "claude-opus-4-5", ClaudeModel.Default("claude-opus-4-5") },
        { "claude-opus-4-1", ClaudeModel.Default("claude-opus-4-1") },
        { "claude-opus-4", ClaudeModel.Default("claude-opus-4") },
        { "claude-sonnet-5", ClaudeModel.Default("claude-sonnet-5") },
        { "claude-sonnet-4-6", ClaudeModel.Default("claude-sonnet-4-6") },
        { "claude-sonnet-4-5", ClaudeModel.Default("claude-sonnet-4-5") },
        { "claude-sonnet-4", ClaudeModel.Default("claude-sonnet-4") },
        { "claude-haiku-4-5", ClaudeModel.Default("claude-haiku-4-5") },

        { "gpt-6-astra", ChatGptModel.Default("gpt-6-astra") },
        { "gpt-5.6-sol", ChatGptModel.Default("gpt-5.6-sol") },
        { "gpt-5.6-terra", ChatGptModel.Default("gpt-5.6-terra") },
        { "gpt-5.6-luna", ChatGptModel.Default("gpt-5.6-luna") },
        { "gpt-5.5", ChatGptModel.Default("gpt-5.5") },

        { "gemini-2.5-pro", GeminiModel.Default("gemini-2.5-pro", "Google") },
        { "gemini-2.5-flash", GeminiModel.Default("gemini-2.5-flash", "Google") },
        { "gemini-2.5-flash-lite", GeminiModel.Default("gemini-2.5-flash-lite", "Google") },

        { "deepseek-flash", DeepSeekModel.Default("deepseek-flash") },
    };

    /// <summary>
    /// All <see cref="Models.IModel"/>s
    /// </summary>
    public static IReadOnlyDictionary<string, IModel> Models => PrivateModelMakers;

    /// <summary>
    /// All available <see cref="Models.IModel"/>s
    /// </summary>
    public static IEnumerable<IModel> AvailableModels => Models.Values.Where(m => m.Available);

    /// <summary>
    /// The current <see cref="IHarness"/>
    /// </summary>
    public IHarness Harness { get; } = harness;

    /// <summary>
    /// The default prompt of the agent
    /// </summary>
    public string DefaultPrompt { get; } = defaultPrompt;

    // TODO : Enum ??
    // public string Effort { get; set; }

    /// <summary>
    /// Forks an agent at a point in a conversation.
    /// </summary>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent Fork() => WithNewModel(Model);

    /// <summary>
    /// Create a new Agent with a new model
    /// </summary>
    /// <param name="model"></param>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent WithNewModel(IModel model)
    {
        Agent agent = new(Token, model, Harness, DefaultPrompt);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    /// <summary>
    /// Create a new Agent that has permission.
    /// </summary>
    /// <param name="model"></param>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent WithPermission()
    {
        // Finds the first Agent or Vendor that has permission
        // TODO : Check existing models
        IModel model = default!;
        Agent agent = new(Token, model, Harness, DefaultPrompt);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    /// <summary>
    /// Sends a message to the assigned model
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public async Task<IEnumerable<ITurn>> SendAsync(string message, CancellationToken token)
    {
        if (!PlugInRegistry.HasPermission(Token))
            throw new PermissionException($"PlugIn {2} does not have permission");

        if (!UserPermissions.IsPermitted(Model.Vendor, Model.Name))
            throw new PermissionException("Model or Vendor does not have permission.");

        List<ITurn> startTurns = new(Turns);

        // The loop hands back the whole transcript, so system turns added on every send would stack up.
        if (startTurns.Count == 0)
        {
            if (!string.IsNullOrEmpty(DefaultPrompt))
                startTurns.Add(new SystemTurn(DefaultPrompt));

            if (SkillsPrompt(Harness.Skills) is string skillsPrompt)
                startTurns.Add(new SystemTurn(skillsPrompt));
        }

        startTurns.Add(new MessageTurn(message));

        IEnumerable<ITurn> turns = await Harness.LoopAsync(this, startTurns, token).ConfigureAwait(false);
        PrivateTurns.Clear();
        PrivateTurns.AddRange(turns);

        return turns;
    }

    private string? SkillsPrompt(IReadOnlyDictionary<string, ISkill> skills)
    {
        if (skills.Count == 0) return null;

        // If read_skill is not available, return null.
        if (!Harness.Mcps.Values.Any(m => m.Tools.ContainsKey("read_skill"))) return null;

        StringBuilder prompt = new("The following skills are available. Call read_skill with a skill's name to load its full instructions before using it.\n");
        foreach (ISkill skill in skills.Values)
            prompt.Append($"\n- {skill.Name}: {skill.Description}");

        return prompt.ToString();
    }

    /// <summary>
    /// Returns a claude desktop agent that uses the desktop harness
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetClaudeDesktopAgent(PlugIns.PlugInToken token, string prompt = "")
        => new Agent(token, new ClaudeDesktopModel("opus"), new ClaudeHarness(), prompt);

    /// <summary>
    /// Returns a CodexDesktop agent that uses a <see cref="CodexHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    // public static Agent GetCodexDesktopAgent(PlugIns.PlugInPermission token, )
    //     => new Agent(token, new CodexDesktopModel("gpt-6"), new CodexHarness());

    /// <summary>
    /// Returns a Claude agent that uses a <see cref="GenericHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetClaudeAgent(PlugIns.PlugInToken token, string model, string prompt)
        => new Agent(token, ClaudeModel.Default(model), new GenericHarness(token), prompt);

    /// <summary>
    /// Returns a ChatGPT agent that uses a <see cref="GenericHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetChatGptAgent(PlugIns.PlugInToken token, string model, string prompt)
        => new Agent(token, ChatGptModel.Default(model), new GenericHarness(token), prompt);

    /// <summary>
    /// Returns a Gemini agent that uses a <see cref="GenericHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetGeminiAgent(PlugIns.PlugInToken token, string model, string prompt)
        => new Agent(token, GeminiModel.Default(model, "Google"), new GenericHarness(token), prompt);

    /// <summary>
    /// Returns a DeepSeek agent that uses a <see cref="GenericHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetDeepSeekAgent(PlugIns.PlugInToken token, string model, string prompt)
        => new Agent(token, DeepSeekModel.Default(model), new GenericHarness(token), prompt);

    /// <summary>
    /// Returns a LMStudio agent that uses a <see cref="GenericHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent GetLMStudioAgent(PlugIns.PlugInToken token, string model, string prompt)
        => new Agent(token, LMStudioModel.Default(model), new GenericHarness(token), prompt);

    /// <summary>
    /// Returns an Agent that uses the appropriate <see cref="IHarness"/>
    /// </summary>
    /// <param name="prompt">The default prompt</param>
    /// <returns>An Agent</returns>
    public static Agent FromModel(PlugInToken token, IModel model, string prompt) => model switch
    {
        ClaudeDesktopModel => GetClaudeDesktopAgent(token, prompt),
        // CodexDesktopModel => GetCodexDesktopAgent(prompt),

        // TODO : fallthrough might be sufficient
        DeepSeekModel => GetDeepSeekAgent(token, model.Name, prompt),
        ClaudeModel => GetClaudeAgent(token, model.Name, prompt),
        ChatGptModel => GetChatGptAgent(token, model.Name, prompt),
        GeminiModel => GetGeminiAgent(token, model.Name, prompt),
        LMStudioModel => GetLMStudioAgent(token, model.Name, prompt),

        _ => new Agent(token, model, new GenericHarness(token), prompt),
    };


}
