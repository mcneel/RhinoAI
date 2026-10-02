using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool to list available resources
/// </summary>
public sealed record ListResources(IHarness Harness) : Tool("list_resources",
                                "Used for listing the available resources in all MCPs",
                                true,
                                false,
                                [
                                    new ("mcp_name", "The MCP Name to search, optional", ToolArgType.String, false),
                                    new ("keywords", "Keywords separated by commas ", ToolArgType.String, false),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        string[] keywords = [];
        if (args.TryGetString("keywords", out string keywordString))
        {
            keywords = keywordString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }

        List<(IMcp Mcp, IEnumerable<IResource> Resources)>? resources = null;
        if (args.TryGetString("mcp_name", out string mcp_name))
        {
            if (!Harness.Mcps.TryGetValue(mcp_name, out IMcp? mcp) || mcp is null)
                return ToolReturn.Failure($"No MCP named '{mcp_name}'.", $"Use one of: {string.Join(", ", Harness.Mcps.Keys)}, or leave mcp_name out to search all MCPs.");

            resources = [(mcp, mcp.Resources.Values)];
        }

        if (resources is null)
        {
            resources = [];
            foreach (IMcp mcp in Harness.Mcps.Values)
            {
                resources.Add((mcp, mcp.Resources.Values));
            }
        }

        if (resources is null || resources.Count == 0)
            return ToolReturn.Success("No resources available with the given args");

        IEnumerable<JsonNode> nodes = resources.SelectMany((kvp) => DescribeAndFilter(kvp, keywords));

        JsonArray listing = new(nodes.ToArray());
        return ToolReturn.Success(listing.ToJsonString());
    }

    private static IEnumerable<JsonNode> DescribeAndFilter((IMcp Mcp, IEnumerable<IResource> Resources) tuple, string[] keywords)
    {
        foreach (IResource resource in tuple.Resources)
        {
            if (keywords.Length > 0 && !MatchesAny(resource, keywords)) continue;
            yield return Describe(tuple.Mcp, resource);
        }
    }

    private static bool MatchesAny(IResource resource, string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (MatchesAny(resource.Description, keyword)) return true;
            if (MatchesAny(resource.Title, keyword)) return true;
            // if (MatchesAny(resource.Uri.LocalPath, keyword)) return true;
        }

        return false;
    }

    private static bool MatchesAny(string description, string keyword)
        => description.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static JsonObject Describe(IMcp mcp, IResource resource) => new JsonObject
    {
        ["mcp"] = mcp.Name,
        ["uri"] = resource.Uri.ToString(),
        ["title"] = resource.Title,
        ["description"] = resource.Description,
        ["mimeType"] = resource.MimeType,
    };

}
