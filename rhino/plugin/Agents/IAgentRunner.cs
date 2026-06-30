using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

internal interface IAgentRunner : IDisposable
{
    /// <summary>The agent's name (its registry key, e.g. claude/codex/gemini).</summary>
    public string Name { get; }

    /// <summary>Prompts the agent with a user message (text + inline attachments).</summary>
    public Task PromptAsync(UserMessage message, string mcpUrl, string cwd);

    /// <summary>Cancels the agent's currently running turn, if any.</summary>
    public void Cancel();

    /// <summary>The live transcript for this agent, fed by its event stream.</summary>
    public Conversation Conversation { get; }
}
