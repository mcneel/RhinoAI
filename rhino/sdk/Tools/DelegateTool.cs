using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

public class DelegateTool : ITool
{

    public string Name { get; } = "delegate";
    public string Description { get; } = "Used for delegating tasks with 0 context. This tool creates a sub agent with read-only permissions";
    public bool ReadOnly { get; } = true;
    public bool Destructive { get; } = false;
    public ToolArg[] Args { get; } = [
        new ToolArg("model", "The Model for the SubAgent", ToolArgType.String, true),
        new ToolArg("context", "The Context for the SubAgent", ToolArgType.String, true),
    ];

    public DelegateTool()
    {
        
    }
    

    public async Task<ToolReturn> UseAsync(IReadOnlyDictionary<string, object> args, CancellationToken token)
    {
        if (!args.TryGetAs(Args[0].Name, out string model))
            return ToolReturn.Failure("model parameter is mandatory", "Call delegate again with model set to the model the sub agent should run on.");

        if (!args.TryGetAs(Args[1].Name, out string context))
            return ToolReturn.Failure("context parameter is mandatory", "Call delegate again with context set to everything the sub agent needs, since it starts with none.");

        Agent agent = new (model, new ReadOnlyHarness());
        IEnumerable<ITurn> turns = await agent.SendAsync(context, token).ConfigureAwait(false);

        StringBuilder report = new();
        foreach (ITurn turn in turns)
        {
            if (turn is MessageTurn message)
                report.AppendLine(message.Message);
        }

        return report.Length == 0
            ? ToolReturn.Failure("The sub agent finished without reporting anything.", "Delegate again with a context that states what the sub agent should report back.")
            : ToolReturn.Success(report.ToString().TrimEnd());
    }

}
