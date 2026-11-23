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
public sealed class RhinoHarness : Harness
{

    public RhinoHarness(PlugIns.PlugInToken token) : base(token)
    {
        RhinoMcp rhinoMcp = new ();
        AddMcp(rhinoMcp);

        // InitSkills();
    }

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

    public static List<string> Classifications { get; } = ["Code", "Geometry", "Image", "Other"];

    public override async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is Models.DesktopModel)
        {
            yield return new MessageTurn("Desktop models cannot be used with custom harnesses", RoleType.System);
            yield break;
        };

        List<ITurn> turns = start is List<ITurn> s ? s : new(start);

        // Classify the task? Inject some more info
        if (IsFirstTurn(turns))
        {
            await AddMcps(turns, token);

            List<ITurn> firstTurns = ShrinkForClassification(turns);
            string classifyPrompt = "Classify the following task. Return one of the following words to describe it: " +
                                    "Code\n Geometry\n Image\n Other"; // TODO : Add more classifications
            firstTurns.Insert(0, new MessageTurn(classifyPrompt, RoleType.System));

            bool done = false;
            await foreach (ITurn turn in agent.Model!.StreamAsync(this, firstTurns, token))
            {
                if (done) break;
                if (turn is TurnStart) continue;
                if (turn is TurnEnd) continue;
                if (turn is ToolTurn) continue;

                foreach (string classification in Classifications)
                {
                    if (!turn.Data.Contains(classification, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(classification, Classifications.Last(), StringComparison.OrdinalIgnoreCase)) continue;
                    turns.Insert(0, ClassificationPrompt(classification));
                    done = true;
                    break;
                }
            }
        }

        if (IsOverMaxContext(turns))
        {
            turns = await Compact(agent, turns, token);
        }

        // It seems to me that the Rhino Harness should include a pretty good default prompt
        // TODO : turns.Insert(0, GetRhinoHarnessPrompt());
        //        Make the prompt a .md resource

        // Should this harness include a custom Loop?
        // It might make sense to refuse certain tool usage
        await foreach (ITurn turn in Loop.StreamAsync(agent, turns, token))
        {
            yield return turn;
        }
    }

    private async Task AddMcps(List<ITurn> turns, CancellationToken token)
    {
        List<string> toolNameList = [];
        foreach(Mcp mcp in Mcps.Values)
        {
            if (!mcp.Enabled) continue;
            await mcp.InitAsync(token);
            foreach(ITool tool in mcp.Tools.Values)
            {
                toolNameList.Add($"{mcp.Name}__{tool.Name}");
            }
        }

        string toolPrompt = $"---\nMCP TOOL LIST\n" +
                            string.Join('\n', toolNameList) +
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

            _ => string.Empty
        };

        return new MessageTurn(message, RoleType.System);
    }

    private static string GetPrompt(string fileName)
    {
        string resourceName = $"Rhino.AI.Prompts.{fileName}";
        using Stream stream = typeof(RhinoHarness).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{resourceName}' is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static bool IsFirstTurn(IEnumerable<ITurn> start)
        => !start.Any(t => t is TurnEnd);

    private async Task<List<ITurn>> Compact(Agent agent, List<ITurn> turns, CancellationToken token)
    {
        if (agent.Model is null)
        {
            return [new TurnEnd(StopReason.Error, "Model on agent is null")];
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
        if (index < 0) return turns;

        List<ITurn> compactableTurns = turnsAsList.GetRange(0, index);
        List<ITurn> keepTurns = turnsAsList.GetRange(index, turnsAsList.Count - index);

        // TODO : Maybe inform the user that nothing could be compacted?
        if (compactableTurns.Count <= 0) return turns;

        List<ITurn> compactedTurns = [];

        // TODO : Wrap this in a while loop? It might require a few compactions depending on the mnodel?
        {
            // TODO : Include a compaction size goal?
            compactableTurns.Insert(0, new MessageTurn("The following messages, excluding this one, must be compacted." +
                                                        "Convert all of them into a summary, excluding any unnecessary information such as tool calls and Turn Ends." +
                                                        "Do not include this preliminary information in the summary you return."));

            await foreach (ITurn turn in agent.Model!.StreamAsync(this, compactableTurns, token))
            {
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

        return keepTurns;
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

}
