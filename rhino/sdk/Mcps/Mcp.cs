using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Rhino.AI;

/// <summary>
/// An MCP is a package of related tools and resources, usually connecting an AI Agent to another application or service
/// </summary>
public abstract class Mcp : IDisposable
{

    /// <summary>The name of the MCP.</summary>
    public string Name { get; }

    /// <summary>DO NOT USE</summary>
    private Mcp() : this("") {}

    internal Mcp(string name)
    {
        Name = Tool.CorrectName(name);
    }

    protected Dictionary<string, ITool> PrivateTools { get; } = [];

    /// <summary>The tools offered by this MCP</summary>
    public IReadOnlyDictionary<string, ITool> Tools => PrivateTools;

    /// <summary>
    /// Registers a tool for the MCP
    /// </summary>
    /// <param name="tool">The tool to register</param>
    /// <returns>True on success, false otherwise</returns>
    public bool RegisterTool(ITool tool)
    {
        if (!PrivateTools.TryAdd(tool.Name, tool))
            return false;

        PrivateTools[tool.Name] = tool;
        return true;
    }

    protected Dictionary<string, IResource> PrivateResources { get; } = [];

    /// <summary>Resources offered by this MCP</summary>
    public IReadOnlyDictionary<string, IResource> Resources => PrivateResources;

    /// <summary>
    /// Registers a resource for the MCP
    /// </summary>
    /// <param name="resource">The resource to register</param>
    /// <returns>True on success, false otherwise</returns>
    public bool RegisterResource(IResource resource) => false;

    /// <summary>
    /// Initializes the MCP.
    /// </summary>
    /// <param name="token">A cancellation token</param>
    /// <returns>True on success</returns>
    public virtual Task<bool> InitAsync(CancellationToken token) => Task.FromResult(true);

    /// <summary>
    /// Runs the tool
    /// </summary>
    /// <param name="agent">The Agent calling the tool, whose definition decides permissions</param>
    /// <param name="harness">The Harness used to ask the user when permission is required</param>
    /// <param name="toolName">The name of the tool</param>
    /// <param name="args">The given args for the specific tool call</param>
    /// <param name="token">A cancellation token</param>
    /// <returns><see cref="ToolReturn"/></returns>
    public async Task<ToolReturn> RunToolAsync(Agent agent, Harness harness, string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!PrivateTools.TryGetValue(toolName, out ITool? tool) || tool is null)
            return ToolReturn.Failure($"MCP '{Name}' has no tool '{toolName}'.", "Call one of the tools offered in this session.");

        List<IToolArg> supplied = new(args.Count);
        foreach (IToolArg argument in args)
        {
            if (Declared(tool, argument.Name) is not ToolParameter declared)
                return ToolReturn.Failure($"Tool '{toolName}' has no argument '{argument.Name}'.", $"Call '{toolName}' again with only the arguments it declares.");

            supplied.Add(argument);
        }

        foreach (ToolParameter declared in tool.Args)
        {
            if (declared.Required && !supplied.Any(s => string.Equals(s.Name, declared.Name)))
                return ToolReturn.Failure($"Tool '{toolName}' requires argument '{declared.Name}'.", $"Call '{toolName}' again with '{declared.Name}' supplied.");
        }

        List<IToolArg> argsList = args.ToList();

        Permissability permissability = PermissionSet.ResolvePermissionChain(agent.Definition, this, tool, argsList);
        if (permissability == Permissability.Deny) return ToolReturn.Refused(toolName);
        if (permissability == Permissability.Ask && harness.AskUser is not null)
        {
            PermissionRequest request = new(this, tool, argsList);
            harness.RaiseBeforeAskUserPermission(request);
            await harness.AskUser.Invoke(request, token);
            if (!request.HasPermission) return ToolReturn.Refused(toolName);
        }

        return await tool.UseAsync(supplied, token).ConfigureAwait(false);
    }

    private static ToolParameter? Declared(ITool tool, string name)
    {
        foreach (ToolParameter arg in tool.Args)
        {
            if (arg.Name == name)
                return arg;
        }

        return null;
    }

    // Boxing by hand is load bearing: without it every arm converts to JsonNode instead, and a string argument reaches the tool as a JsonValue.
    private static object? ToValue(ToolParameter arg, string value) => arg.Type switch
    {
        ToolArgType.String => value,
        ToolArgType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? (object)number : null,
        ToolArgType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer) ? (object)integer : null,
        ToolArgType.Boolean => bool.TryParse(value, out bool flag) ? (object)flag : null,
        ToolArgType.Array or ToolArgType.Object => ParseJson(value),
        _ => throw new ArgumentOutOfRangeException(nameof(arg), arg.Type, "Unknown argument type."),
    };

    private static JsonNode? ParseJson(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Nothing to release: the tools are delegates, not a process or a socket.
    public virtual void Dispose()
    {
    }

}
