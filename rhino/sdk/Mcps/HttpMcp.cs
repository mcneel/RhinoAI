using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Rhino.AI;

/// <summary>
/// A HTTP MCP that drives a server
/// </summary>
public sealed class HttpMcp(string name, Uri url) : IMcp
{

    public string Name { get; } = name;

    private Dictionary<string, ITool> PrivateTools { get; } = [];
    public IReadOnlyDictionary<string, ITool> Tools => PrivateTools;

    public Uri Url { get; } = url;

    private Process? Process { get; set; }
    private bool Disposed { get; set; }
    private int LastId { get; set; }

    private StreamReader? Output => Process?.StandardOutput;
    private StreamReader? Error => Process?.StandardError;
    private StreamWriter? Input => Process?.StandardInput;

    public async Task<bool> InitAsync(CancellationToken token)
    {
        return false;
    }

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        return ToolReturn.Refused();
    }

    public bool RegisterTool(ITool tool)
        => PrivateTools.TryAdd(tool.Name, tool);

    public void Dispose()
    {

    }

}
