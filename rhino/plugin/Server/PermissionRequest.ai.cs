using System.Threading.Tasks;

namespace Rhino.AI;

// One "may I?" waiting in a conversation: what the agent wants to do, and the answer the panel will
// give it. Runtime only, never persisted — a request nobody is still waiting on is not a request.
internal sealed class PermissionRequest
{
    private TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PermissionRequest(string tool, string title, string detail)
    {
        Tool = tool;
        Title = title;
        Detail = detail;
    }

    /// <summary>Wire name of the tool asking, e.g. script_editor_run.</summary>
    public string Tool { get; }

    /// <summary>What the card calls it, e.g. "Run Script".</summary>
    public string Title { get; }

    /// <summary>The arguments, as the user should see them. Empty when the call takes none.</summary>
    public string Detail { get; }

    /// <summary>The user's answer. False when the turn is cancelled or the panel goes away.</summary>
    public Task<bool> Decision => Answer.Task;

    /// <summary>First answer wins, so a cancel racing a click cannot flip a decision already given.</summary>
    public void Resolve(bool allowed) => Answer.TrySetResult(allowed);
}
