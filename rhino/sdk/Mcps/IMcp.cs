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

    public Task<bool> InitAsync(CancellationToken token);

    public void RegisterTool(ITool tool);

    public Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token);

}
