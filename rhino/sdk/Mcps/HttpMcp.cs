using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;

namespace Rhino.AI;

/// <summary>
/// A HTTP MCP that drives a server
/// </summary>
public sealed class HttpMcp(string name, Uri url, IDictionary<string, string> headers) : Mcp(name)
{

    public HttpMcp(string name, Uri url) : this(name, url, new Dictionary<string, string>())
    {
    }

    public Uri Url { get; } = url;

    public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>(headers);

    private HttpListener Listener { get; } = new();

    private bool Initialized { get; set; } = false;
    public override async Task<bool> InitAsync(CancellationToken token)
    {
        if (Initialized) return true;

        // TODO : Claude - Is HttpListener correct?
        Listener.Start();

        // TODO : Would auth be done here?

        return false;
    }

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
        => throw new NotImplementedException();

    public override bool RegisterTool(ITool tool) => false;

    ~HttpMcp()
    {
        // TODO : Claude = May be a better way
        Dispose();
    }

    public override void Dispose()
    {
        // AI : Close Server
    }

}
