using System;
using System.Collections.Generic;

using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FuzzySharp;
using FuzzySharp.PreProcess;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool used for delegating tasks to a SubAgent.
/// </summary>
public sealed record DelegateTool() : Tool("delegate",
                                "Used for delegating tasks with 0 context. This tool creates a sub agent with read-only permissions",
                                true,
                                false,
                                [
                                    // Agent
                                    new ("model", "The Model for the SubAgent", ToolArgType.String, true),

                                    // Context
                                    new ("prompt", "The default prompt for the agent", ToolArgType.String, true),

                                    // TODO : Skills + Tools to enable
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        string context = string.Empty;

        if (!args.TryGetString(Args[0].Name, out string modelName))
            return ToolReturn.Failure("model parameter is mandatory", "Call delegate again with model set to the model the sub agent should run on.");

        if (!args.TryGetString(Args[1].Name, out string prompt))
            return ToolReturn.Failure("prompt parameter is mandatory", "Call delegate again with context set to everything the sub agent needs, since it starts with none.");

        if (Agent.Models.Count <= 0)
            return ToolReturn.Failure("This session cannot create sub agents.", "Do the work yourself instead of delegating it.");

        if (!Agent.Models.TryGetValue(modelName, out Models.IModel? model))
        {
            IEnumerable<Models.IModel> models = Agent.Models.Values.Where(m => m.Name.Contains(modelName, StringComparison.InvariantCultureIgnoreCase) && m.Available);
            IEnumerable<Models.IModel> availableModels = (models.Any() ? models : Agent.Models.Values).Where(m => m.Available);

            string modelList = string.Join(";", availableModels);

            IEnumerable<Models.IModel> likelyModels = Agent.Models.Values
                .OrderByDescending(m => Fuzz.WeightedRatio(modelName, m.Name, PreprocessMode.Full))
                .Take(3);
            IEnumerable<string> likelyModelNames = likelyModels.Select(t => t.Name);
            string likelyModelString = string.Join(" or ", likelyModelNames);
            return ToolReturn.Failure($"{modelName} is not available", $"Did you mean {likelyModelString}?");
        }
        
        if (!model.Available)
        {
            return ToolReturn.Failure($"{model.Name} is not available", $"Try one of {string.Join(", ", Agent.AvailableModels.Select(m => m.Name))}");
        }

        Agent agent = Agent.FromModel(model, prompt);
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
