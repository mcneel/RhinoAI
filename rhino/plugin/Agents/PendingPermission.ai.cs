using System.Threading.Tasks;

namespace Rhino.AI;

// Blocking, unlike PendingQuestion: the harness awaits Decision, so whoever shows this must eventually call Decide.
internal sealed class PendingPermission
{
    private TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingPermission(string mcpName, string toolName, IReadOnlyList<string> declaredArgs, string argsJson)
    {
        McpName = mcpName;
        ToolName = toolName;
        DeclaredArgs = declaredArgs;
        ArgsJson = argsJson;
    }

    public string McpName { get; }
    public string ToolName { get; }
    public IReadOnlyList<string> DeclaredArgs { get; }
    public string ArgsJson { get; }

    public Task<bool> Decision => Answer.Task;

    public bool IsDecided => Answer.Task.IsCompleted;

    public void Decide(bool allow) => Answer.TrySetResult(allow);
}
