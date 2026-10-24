using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using FuzzySharp;
using FuzzySharp.PreProcess;

using Rhino.AI.Models;
using System.Diagnostics.CodeAnalysis;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool used for delegating tasks to a SubAgent.
/// </summary>
public sealed record DelegateTool(PlugIns.PlugInToken token) : Tool("delegate",
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

    private PlugIns.PlugInToken Token { get; } = token;

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        string context = string.Empty;

        if (!args.TryGetString(Args[0].Name, out string modelName))
            return ToolReturn.Failure("model parameter is mandatory", "Call delegate again with model set to the model the sub agent should run on.");

        if (!args.TryGetString(Args[1].Name, out string prompt))
            return ToolReturn.Failure("prompt parameter is mandatory", "Call delegate again with context set to everything the sub agent needs, since it starts with none.");

        if (!Instances.Agents.Definitions.Any(d => d.AvailableModels.Count > 0))
        {
            return ToolReturn.Failure("This session cannot create sub agents.", "Do the work yourself instead of delegating it.");
        }

        IEnumerable<(IAgentDefinition, IModel)> filteredModels = AvailableModels(modelName);
        if (filteredModels is null || !filteredModels.Any())
        {
            IEnumerable<IModel> availableModels = AvailableModels()
                                                    .Select(i => i.Item2).ToHashSet(new ModelComparer());
            IEnumerable<IModel> likelyModels = availableModels
                                                    .OrderByDescending(m => Fuzz.WeightedRatio(modelName, m.Name, PreprocessMode.Full))
                                                    .Take(3);
            IEnumerable<string> likelyModelNames = likelyModels.Select(t => t.Name);
            string likelyModelString = string.Join(" or ", likelyModelNames);
            return ToolReturn.Failure($"{modelName} is not available", $"Did you mean {likelyModelString}?");
        }

        // TODO : Choose model
        IAgentDefinition definition = filteredModels.First().Item1;
        Agent? agent = definition.WithModel(Token, modelName);
        if (agent is null)
        {
            // TODO : Improve this message. Why it this ever happen?
            return ToolReturn.Failure($"{modelName} is not available", $"Try one of {string.Join(", ", filteredModels.Select(m => m.Item2.Name))}");
        }

        IEnumerable<ITurn> turns = await agent.SendAsync(context, token).ConfigureAwait(false);

        StringBuilder report = new();
        foreach (ITurn turn in turns)
        {
            if (turn is MessageTurn message)
            {
                report.AppendLine(message.Message);
            }
        }

        return report.Length == 0
            ? ToolReturn.Failure("The sub agent finished without reporting anything.", "Delegate again with a context that states what the sub agent should report back.")
            : ToolReturn.Success(report.ToString().TrimEnd());
    }

    private class ModelComparer : IEqualityComparer<IModel>
    {
        public bool Equals(IModel? x, IModel? y)
        {
            if (x is null && y is null) return true;
            if (x is null && y is not null) return false;
            if (y is null && x is not null) return false;
            
            return string.Equals(x!.Name, y!.Name, System.StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode([DisallowNull] IModel obj) => obj.Name.GetHashCode();
        
    }

    private static List<(IAgentDefinition, IModel)> AvailableModels(string? modelName = null)
    {
        // TODO : Put the preferred agent _first_

        List<(IAgentDefinition, IModel)> pairs = new ();
        foreach(IAgentDefinition agentDef in Instances.Agents.Definitions)
        {
            if (!agentDef.Available) continue;
            foreach(ModelDefinition modelDef in agentDef.AvailableModels)
            {
                IModel? model = agentDef.GetModel(modelDef.ModelId);
                if (model is null) continue;
                if (!string.IsNullOrEmpty(modelName))
                {
                    if (!string.Equals(model.Name, modelName, System.StringComparison.OrdinalIgnoreCase)) continue;
                }
                pairs.Add((agentDef, model));
            }
        }

        return pairs;
    }

}
