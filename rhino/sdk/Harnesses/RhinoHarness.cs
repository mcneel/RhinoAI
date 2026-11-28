using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rhino.AI;

// NOTE : This harness cannot be used with Desktop CLI Agents

/// <summary>
/// A Rhino specific harness, including all Rhino tooling
/// </summary>
public sealed class RhinoHarness : Harness, IDisposable
{

    private RhinoMcp Router { get; } = new();

    public RhinoHarness(PlugIns.PlugInToken token) : base(token)
    {
        AddMcp(Router);

        // InitSkills();
    }

    public void Dispose() => Router.Dispose();

    private string? CachedResolvedRouter { get; set; }
    private string ResolveRouter
    {
        get
        {
            if (string.IsNullOrEmpty(CachedResolvedRouter))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string mcneel = Path.Combine(appData, "McNeel");
                string rhinoceros = Path.Combine(mcneel, "Rhinoceros");
                string ai = Path.Combine(rhinoceros, "ai");
                string bin = Path.Combine(ai, "bin");
                string router = Path.Combine(bin, "rhino-mcp-router");
                CachedResolvedRouter = router;
            }

            return CachedResolvedRouter;
        }
    }

    public static List<string> Classifications { get; } = ["Code", "Geometry", "Image", "Grasshopper", "Other"];

    public override async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is Models.DesktopModel)
        {
            yield return new MessageTurn("Desktop models cannot be used with custom harnesses", RoleType.System);
            yield break;
        }
        ;

        List<ITurn> turns = start is List<ITurn> s ? s : new(start);

        // Classify the task? Inject some more info
        if (IsFirstTurn(turns))
        {
            await AddMcps(turns, token);
            turns.AddRange(await ClassifyRequest(agent, turns, token));
            InsertRhinoHarnessPrompt(ref turns);
        }

        // TODO : Resolve
        // if (IsOverMaxContext(turns))
        // {
        //     (turns, int? compactionTokens) = await Compact(agent, turns, token);
        //     harnessTokens = AddTokens(harnessTokens, compactionTokens);
        // }

        // Should this harness include a custom Loop?
        // It might make sense to refuse certain tool usage
        await foreach (ITurn turn in Loop.StreamAsync(agent, turns, token))
        {
            yield return turn;
        }
    }

    internal async Task<IEnumerable<ITurn>> ClassifyRequest(Agent agent, List<ITurn> inputTurns, CancellationToken token)
    {
        List<ITurn> turns = [];
        bool done = false;

        List<ITurn> classifyTurns = ShrinkForClassification(inputTurns);
        string classifyPrompt = $"Classify the following task yourself. Do not use any skills/resources/tools. Return one of the following words to describe it: {string.Join("\n", Classifications)}";
        classifyTurns.Insert(0, new MessageTurn(classifyPrompt, RoleType.System));

        await foreach (ITurn turn in agent.Model!.StreamAsync(this, classifyTurns, token))
        {
            if (turn is UsageTurn)
            {
                turns.Add(turn);
                continue;
            }

            if (done || turn is TurnEnd or TurnStart or ToolTurn or ThinkingTurn) continue;

            foreach (string classification in Classifications)
            {
                if (!turn.Data.Contains(classification, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(classification, Classifications.Last(), StringComparison.OrdinalIgnoreCase)) continue;
                
                turns.Add(ClassificationPrompt(classification));
                done = true;
                break;
            }
        }

        return turns;
    }

    private static int? AddTokens(int? total, int? count) => total is null ? count : total + (count ?? 0);

    private async Task AddMcps(List<ITurn> turns, CancellationToken token)
    {
        List<string> toolNameList = [];
        foreach (Mcp mcp in Mcps.Values)
        {
            if (!mcp.Enabled) continue;
            await mcp.InitAsync(token);
            foreach (ITool tool in mcp.Tools.Values)
            {
                toolNameList.Add($"{mcp.Name}__{tool.Name}");
            }
        }

        string toolPrompt = $"---\nMCP TOOL LIST\n" +
                            string.Join("\n", toolNameList) +
                            "---";

        turns.Add(new MessageTurn(toolPrompt, RoleType.System));
    }

    private static List<ITurn> ShrinkForClassification(List<ITurn> turns)
    {
        List<ITurn> finalTurns = new();
        foreach (ITurn turn in turns)
        {
            if (turn is TurnStart) continue;
            if (turn is TurnEnd) continue;
            // TODO : Remove the thinking turns?
            if (turn is MessageTurn message)
            {
                if (message.Content.Any(mc => mc is ImageContent))
                {
                    List<IMessageContent> contents = new();
                    foreach (IMessageContent content in message.Content)
                    {
                        if (content is ImageContent)
                        {
                            contents.Add(new TextContent("This image was removed to save tokens"));
                            continue;
                        }

                        contents.Add(content);
                    }

                    finalTurns.Add(new MessageTurn(contents, message.Role, message.TimeStamp, message.Duration, message.TokenCount));
                    continue;
                }
            }

            finalTurns.Add(turn);
        }

        return finalTurns;
    }

    // TODO : This needs way more info
    private static MessageTurn ClassificationPrompt(string classification)
    {
        string message = classification.ToLowerInvariant() switch
        {
            "code" => GetPrompt("RhinoHarnessCodePrompt.md"),
            "geometry" => GetPrompt("RhinoHarnessGeometryPrompt.md"),
            "image" => GetPrompt("RhinoHarnessImagePrompt.md"),
            "grasshopper" => GetPrompt("RhinoHarnessGrasshopperPrompt.md"),

            _ => string.Empty
        };

        return new MessageTurn(message, RoleType.System);
    }

    private async Task<(List<ITurn> Turns, int? TokenCount)> Compact(Agent agent, List<ITurn> turns, CancellationToken token)
    {
        if (agent.Model is null)
        {
            return ([new TurnEnd(StopReason.Error, "Model on agent is null")], null);
        }

        List<ITurn> turnsAsList = turns.ToList();
        int index = -1;
        for (int i = turnsAsList.Count - 1; i > 0; i--)
        {
            if (turnsAsList[i] is TurnEnd)
            {
                index = i;
                break;
            }
        }

        // TODO : Maybe inform the user this starting context is too high?
        if (index < 0) return (turns, null);

        List<ITurn> compactableTurns = turnsAsList.GetRange(0, index);
        List<ITurn> keepTurns = turnsAsList.GetRange(index, turnsAsList.Count - index);

        // TODO : Maybe inform the user that nothing could be compacted?
        if (compactableTurns.Count <= 0) return (turns, null);

        List<ITurn> compactedTurns = [];
        int? tokenCount = null;

        // TODO : Wrap this in a while loop? It might require a few compactions depending on the mnodel?
        {
            // TODO : Include a compaction size goal?
            compactableTurns.Insert(0, new MessageTurn("The following messages, excluding this one, must be compacted." +
                                                        "Convert all of them into a summary, excluding any unnecessary information such as tool calls and Turn Ends." +
                                                        "Do not include this preliminary information in the summary you return."));

            await foreach (ITurn turn in agent.Model!.StreamAsync(this, compactableTurns, token))
            {
                if (turn is TurnEnd end) tokenCount = AddTokens(tokenCount, end.TokenCount);
                compactedTurns.Add(turn);
            }
        }

        string summary = string.Empty;
        foreach (ITurn compactTurn in compactedTurns)
        {
            // TODO : Include thinking turns? // if (compactTurn is not ThinkingTurn message) continue;
            if (compactTurn is not MessageTurn message) continue;
            summary += message.Data;
            summary += "\n";
        }

        if (IsOverMaxContext(compactedTurns))
        {
            // TODO : Do something about this
        }

        keepTurns.Insert(0, new MessageTurn(summary, RoleType.System));

        return (keepTurns, tokenCount);
    }

    private static bool IsOverMaxContext(IEnumerable<ITurn> start)
    {
        // TODO : Does max token count come from the agent or the harness config? I'd think model maybe?
        int MaxTokenForContext = 86_000;
        int tokenCalc = TokenEstimate.FromTurnsPreferringReported(start);
        return tokenCalc > MaxTokenForContext;
    }

    // public override async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
    // {
    //     // TODO : It is possible that a tool call could ask the AI if it's SURE it needs it
    //     //        e.g the Viewport Tool to validate. It's expensive.

    //     throw new NotImplementedException("oops");
    // }

    public override Func<IPermissionRequest, CancellationToken, Task>? AskUser { get; set; }

}
