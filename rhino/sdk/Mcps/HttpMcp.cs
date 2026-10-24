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
public sealed class HttpMcp(string name, Uri url) : Mcp(name)
{

    public Uri Url { get; } = url;

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

    ~HttpMcp()
    {
        // TODO : Claude = May be a better way
        Dispose();
    }

    public override void Dispose()
    {
        // TODO : Close Server
    }

}
