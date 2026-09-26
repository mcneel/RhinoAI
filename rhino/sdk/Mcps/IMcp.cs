using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rhino.AI;

/// <summary>
/// An MCP is a package of related tools and resources, usually connecting an AI Agent to another application or service
/// </summary>
public interface IMcp : IDisposable
{

    /// <summary>The name of the MCP</summary>
    public string Name { get; }

    /// <summary>The tools offered by this MCP</summary>
    public IReadOnlyDictionary<string, ITool> Tools { get; }

    /// <summary>
    /// Initializes the MCP.
    /// </summary>
    /// <param name="token">A cancellation token</param>
    /// <returns>True on success</returns>
    public Task<bool> InitAsync(CancellationToken token);

    /// <summary>
    /// Registers a tool for the MCP
    /// </summary>
    /// <param name="tool">The tool to register</param>
    /// <returns>True on success, false otherwise</returns>
    public bool RegisterTool(ITool tool);

    /// <summary>
    /// Runs the tool
    /// </summary>
    /// <param name="toolName">The case insensitive name of the tool</param>
    /// <param name="args">The given args for the specific tool call</param>
    /// <param name="token">A cancellation token</param>
    /// <returns><see cref="ToolReturn"/></returns>
    public Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token);

}
