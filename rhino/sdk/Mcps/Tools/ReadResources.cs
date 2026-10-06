using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

// TODO : Resource name / key needs work

/// <summary>
/// An MCP tool to read a resource(s)
/// </summary>
public sealed record ReadResources(IHarness Harness) : Tool("read_resources",
                                "Used for reading an MCP resource",
                                true,
                                false,
                                [
                                    new ("mcp_name", "The MCP to read from", ToolArgType.String, true),
                                    new ("resource_name", "The resource name to read", ToolArgType.String, true),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetString(Args[0].Name, out string mcpName))
            return ToolReturn.Failure($"{Args[0].Name} parameter is mandatory", $"Call {Name} again with {Args[0].Name} set to the mcp value from list_resources.");

        if (!args.TryGetString(Args[1].Name, out string resourcePath))
            return ToolReturn.Failure($"{Args[1].Name} parameter is mandatory", $"Call {Name} again with {Args[1].Name} set to a resource from list_resources.");

        if (!Harness.Mcps.TryGetValue(mcpName, out IMcp? mcp) || mcp is null)
            return ToolReturn.Failure($"No MCP named '{mcpName}'.", $"Use one of: {string.Join(", ", Harness.Mcps.Keys)}.");

        if (!mcp.Resources.TryGetValue(resourcePath, out IResource? resource) || resource is null)
            return ToolReturn.Failure($"MCP '{mcpName}' has no resource '{resourcePath}'.", $"Call list_resources with mcp_name '{mcpName}' to see what it offers.");

        ResourceReturn result = await resource.ReadAsync(token).ConfigureAwait(false);
        return result.Success ?
            ToolReturn.Success(result.Data) :
            ToolReturn.Failure(result.Data, "The resource could not be read. Do not retry the same call unchanged.");
    }

}
