using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace Rhino.AI;

/// <summary>
/// The Agent is the controller of all the AI parts.
/// </summary>
public sealed class Agent
{

    private PlugInToken Token { get; }

    private List<ITurn> PrivateTurns { get; } = [];

    /// <summary>
    /// All turns in the conversation thus far
    /// </summary>
    public IReadOnlyList<ITurn> Turns => PrivateTurns;

    private IModel? TurnModel { get; set; }

    /// <summary>
    /// The Model in use by the agent
    /// </summary>
    internal IModel? Model => TurnModel ?? Definition?.CurrentModel;

    /// <summary>
    /// The current <see cref="Harness"/>
    /// </summary>
    public Harness Harness { get; }

    private Guid DefinitionId { get; }

    /// <summary>
    /// The User created Definition of this Agent.
    /// </summary>
    /// <remarks>Do not cache this definition</remarks>
    public IAgentDefinition? Definition => AIInstances.Agents.FromId(DefinitionId);

    /// <summary>
    /// Configuration
    /// </summary>
    public AgentConfig Config { get; } = new();

    internal Agent(IAgentDefinition definition, PlugInToken token, Harness harness)
    {
        Token = token;
        Harness = harness;
        DefinitionId = definition.Id;

        definition.RegisterAgent(this);
    }

    // TODO : Enum ??
    // public string Effort { get; set; }

    /// <summary>
    /// Forks an agent at a point in a conversation.
    /// </summary>
    /// <returns>A forked copy of an Agent with all of the state copied safely.</returns>
    internal Agent? Fork()
    {
        if (Definition is null) return null;

        Agent agent = new(Definition, Token, Harness);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        AgentConfig.Push(this.Config, agent.Config);
        return agent;
    }

    public Agent? AsLatestPreferred()
    {
        Agent? agent = GetPreferredAgent(Token);
        if (agent is null) return null;

        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        AgentConfig.Push(this.Config, agent.Config);

        return agent;
    }

    private SemaphoreSlim TurnGate { get; } = new(1, 1);

    /// <summary>
    /// Sends a message to the assigned model
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>Each <see cref="ITurn"/> as the Harness loop produces it</returns>
    /// <exception cref="InvalidOperationException">Calls to this method may not run simultaneously for the same agent</exception>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public StreamedAgentResult StreamAsync(string message, CancellationToken token)
        => StreamAsync([new TextContent(message)], token);

    /// <summary>
    /// Sends content to the assigned model
    /// </summary>
    /// <param name="content">The content to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>Each <see cref="ITurn"/> as the Harness loop produces it</returns>
    /// <exception cref="InvalidOperationException">Calls to this method may not run simultaneously for the same agent</exception>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public StreamedAgentResult StreamAsync(IMessageContent content, CancellationToken token)
        => StreamAsync([content], token);

    /// <summary>
    /// Sends content to the assigned model
    /// </summary>
    /// <param name="contents">The content parts of the message to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>Each <see cref="ITurn"/> as the Harness loop produces it</returns>
    /// <exception cref="InvalidOperationException">Calls to this method may not run simultaneously for the same agent</exception>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public StreamedAgentResult StreamAsync(IEnumerable<IMessageContent> contents, CancellationToken token)
        => new(StreamTurnsAsync(contents, token));

    /// <summary>
    /// Check if the Agent is ready or not.
    /// If the user removed the agent, disabled the agent, or uninstalled it during runtime, it won't be ready.
    /// </summary>
    /// <param name="reason">The reason for the failure</param>
    /// <returns>true if available</returns>
    public bool AgentIsReady(out string reason)
    {
        IAgentDefinition? def = Definition;

        reason = "User removed the source of this agent";
        if (def is null) return false;

        reason = "User removed the source of this agent";
        if (!AIInstances.Agents.Exists(def)) return false;

        reason = "Definition is not disabled";
        if (!def.Enabled) return false;

        reason = "Agent is not available";
        if (!def.Available) return false;

        reason = "";
        return true;
    }

    private async IAsyncEnumerable<ITurn> StreamTurnsAsync(IEnumerable<IMessageContent> contents, [EnumeratorCancellation] CancellationToken token)
    {
        if (!TurnGate.Wait(0, CancellationToken.None))
            throw new InvalidOperationException($"{nameof(StreamAsync)} cannot be run until the previous call has finished on the same agent.");

        if (!AgentIsReady(out string reason))
        {
            TurnGate.Release();
            throw new InvalidOperationException(reason);
        }

        try
        {
            if (!PlugInRegistry.HasPermission(Token))
                throw new PermissionException($"PlugIn {Token.Name} does not have permission");

            List<ITurn> newTurns = [];

            IAgentDefinition definition = Definition!;
            TurnModel = definition.CurrentModel;

            if (PrivateTurns.Count == 0)
            {
                if (!string.IsNullOrEmpty(definition.DefaultPrompt))
                    newTurns.Add(new SystemTurn(definition.DefaultPrompt));

                if (SkillsPrompt(Harness.Skills) is string skillsPrompt)
                    newTurns.Add(new SystemTurn(skillsPrompt));
            }

            newTurns.Add(new MessageTurn(contents, RoleType.User));

            PrivateTurns.AddRange(newTurns);

            foreach (ITurn turn in newTurns)
                yield return turn;

            await foreach (ITurn turn in Harness.StreamLoopAsync(this, [.. PrivateTurns], token).ConfigureAwait(false))
            {
                Statistics.PlugInStatistics.Collect(this, Token, [turn]);
                PrivateTurns.Add(turn);

                // The session is gone for good, so keeping its id would fail every later call the same way.
                if (turn is TurnEnd { Reason: StopReason.InvalidSession })
                    Config.SessionId = Guid.Empty;

                yield return turn;
            }
        }
        finally
        {
            TurnModel = null;
            TurnGate.Release();
        }
    }

    /// <summary>
    /// Sends a message to the assigned model
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public async Task<IAgentResult> SendAsync(string message, CancellationToken token)
        => await SendAsync([new TextContent(message)], token);

    /// <summary>
    /// Sends content to the assigned model
    /// </summary>
    /// <param name="content">The content to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public async Task<IAgentResult> SendAsync(IMessageContent content, CancellationToken token)
        => await SendAsync([content], token);

    /// <summary>
    /// Sends content to the assigned model
    /// </summary>
    /// <param name="contents">The contents to send</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public async Task<IAgentResult> SendAsync(IEnumerable<IMessageContent> contents, CancellationToken token)
    {
        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamAsync(contents, token).ConfigureAwait(false))
        {
            turns.Add(turn);
        }

        return new AgentResult(turns);
    }

    /// <summary>
    /// Sends a message to the assigned model, opt for <see cref="SendAsync(string, CancellationToken)"/> over this if possible to avoid potential deadlocks
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public IAgentResult Send(string message)
        => Send([new TextContent(message)]);

    /// <summary>
    /// Sends content to the assigned model, opt for <see cref="SendAsync(IEnumerable{IMessageContent}, CancellationToken)"/> over this if possible to avoid potential deadlocks.
    /// </summary>
    /// <param name="contents">The contents to send</param>
    /// <returns>The resulting <see cref="ITurn"/>s created within the Harness loop once finished</returns>
    /// <exception cref="PermissionException">If the requested model or vendor is not allowed an exception will be raised</exception>
    public IAgentResult Send(IEnumerable<IMessageContent> contents)
        => Task.Run(() => SendAsync(contents, default)).GetAwaiter().GetResult();

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
    /// Returns an Agent with the given parameters.
    /// If you are using this, the user should be informed you are making the decision for them.
    /// <see cref="GetPreferredAgent"/> is always the best default.
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <param name="vendor">The vendor of the model</param>
    /// <param name="type">The type of Agent</param>
    /// <returns>An Agent if one is available, otherwise null</returns>
    /// <remarks>If a user deletes the Definition for this agent it will no longer function</remarks>
    public static Agent? GetAgentFromParameters(PlugIns.PlugInToken token, string vendor, AgentType type)
        => AIInstances.Agents.FromParameters(vendor, type)?.GetAgent(token);

    /// <summary>
    /// Returns a claude desktop agent that uses the desktop harness
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetClaudeDesktopAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("Anthropic", AgentType.Cli)?.GetAgent(token);

    /// <summary>
    /// Returns a CodexDesktop agent that uses a <see cref="CodexHarness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetCodexDesktopAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("OpenAI", AgentType.Cli)?.GetAgent(token);

    /// <summary>
    /// Returns a Claude agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetClaudeAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("Anthropic", AgentType.Api)?.GetAgent(token);

    /// <summary>
    /// Returns a ChatGPT agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetChatGptAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("OpenAI", AgentType.Api)?.GetAgent(token);

    /// <summary>
    /// Returns a Gemini agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetGeminiAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("Google", AgentType.Api)?.GetAgent(token);

    /// <summary>
    /// Returns a DeepSeek agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetDeepSeekAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.FromParameters("DeepSeek", AgentType.Api)?.GetAgent(token);

    /// <summary>
    /// Returns a LMStudio agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <param name="vendor">The AI Model vendor</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetLocalAgent(PlugIns.PlugInToken token, string vendor)
        => AIInstances.Agents.FromParameters(vendor, AgentType.Local)?.GetAgent(token);

    /// <summary>
    /// Returns a LMStudio agent that uses a <see cref="AI.Harness"/>
    /// </summary>
    /// <param name="definition">The Agent definition</param>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    internal static Agent? GetCustomAgent(IAgentDefinition definition, PlugIns.PlugInToken token)
    {
        return new(definition, token, new Harness(token));
    }

    // Should the conversation get copied over?
    internal Agent? WithHarness(Harness harness)
    {
        if (Definition is null) return null;
        return new(Definition, Token, harness);
    }

    /// <summary>
    /// Returns the users prefered agent, harness, and overall config
    /// </summary>
    /// <param name="definition"></param>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    public static Agent? FromDefinition(IAgentDefinition definition, PlugIns.PlugInToken token)
        => definition.GetAgent(token);

    /// <summary>
    /// Returns the users prefered agent, harness, and overall config.
    /// </summary>
    /// <param name="token">The permissions token</param>
    /// <returns>An Agent</returns>
    public static Agent? GetPreferredAgent(PlugIns.PlugInToken token)
        => AIInstances.Agents.PreferredDefinition?.GetAgent(token);

    /// <summary>
    /// Empties all previous turns
    /// </summary>
    internal void ClearConversation()
    {
        PrivateTurns.Clear();
    }

}

/// <summary>
/// Agent Configuration
/// </summary>
public sealed record AgentConfig
{

    /// <summary>
    /// Uses the local settings of the Agents Harness
    /// </summary>
    public bool UseLocalSettings { get; set; } = false;

    /// <summary>
    /// The Session Id to resume with
    /// </summary>
    public Guid SessionId { get; set; } = Guid.Empty;

    /// <summary>
    /// Effort the Model 
    /// </summary>
    public string ModelEffort { get; set; } = string.Empty;

    /// <summary>
    /// Pushes Config from one agent to another
    /// </summary>
    public static void Push(AgentConfig from, AgentConfig to)
    {
        to.UseLocalSettings = from.UseLocalSettings;
        to.SessionId = from.SessionId;
    }

}
