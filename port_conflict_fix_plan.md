# RhinoAI Port Conflict on Second Instance: Analysis and Fix Plan

## 1. Description of the Problem
When a second instance of Rhino is launched, it attempts to start the MCP server for the new document. However, it crashes with an exception such as:
`IOException: Failed to bind to address http://127.0.0.1:10500: address already in use.`
(Or `HttpListenerException: Failed to listen on prefix...` depending on the .NET Core implementation).

The second Rhino instance prints `[RhinoAI] Failed to start:` or the Kestrel `IOException` stack trace to the terminal, and fails to provide MCP services.

## 2. Root Cause Analysis
The port assignment logic is located in `rhino/plugin/RhinoAIHost.ai.cs`. 

When the second Rhino instance starts, `Servers.Count` is `0` because the `Servers` dictionary is static and per-process. The port selection logic in `TryGetNextPort` correctly identifies `10500` as the starting candidate.

To determine if `10500` is free, `TryGetNextPort` calls `TryBindCandidate(10500, out port)`, which currently probes the port using a TCP socket (`System.Net.Sockets.TcpListener`):
```csharp
System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, candidate);
listener.Start();
```
**The Flaw:** On Windows, `HttpListener` (and ASP.NET Core Kestrel) registers its endpoints with the kernel-level `HTTP.SYS` driver (or with specific `SO_REUSEADDR` flags). A standard `TcpListener` binding to `127.0.0.1` at the raw Winsock level **will frequently succeed** even if `HTTP.SYS` or another HTTP server has already claimed that port for HTTP traffic.

Because `TcpListener.Start()` does not throw an exception, `TryBindCandidate` incorrectly returns `true` (port is free). `RhinoAIHost` then calls `McpServer.Start(10500)`. 
When `McpServer.Start` attempts to actually bind the HTTP server to `10500`, it collides with the first Rhino instance's HTTP server and throws the fatal `IOException` / `HttpListenerException`.

## 3. Proposed Solution
We need to change how we probe for a free port, or change how we start the server so it automatically falls back when the HTTP binding fails.

### Option A: Use `HttpListener` for Probing
Update `TryBindCandidate` to probe the port using `HttpListener` instead of `TcpListener`. This ensures we are testing the exact same sub-system that `McpServer` will use.

```csharp
private static bool TryBindCandidate(int candidate, out int port)
{
    port = candidate;
    if (candidate == 0)
    {
        // For ephemeral port (candidate=0), TcpListener is still the easiest way to retrieve the assigned port.
        try
        {
            System.Net.Sockets.TcpListener tcp = new(System.Net.IPAddress.Loopback, 0);
            tcp.Start();
            port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return true;
        }
        catch { return false; }
    }

    try
    {
        using HttpListener listener = new();
        listener.Prefixes.Add($"http://localhost:{candidate}/");
        listener.Start();
        listener.Stop();
        return true;
    }
    catch
    {
        return false;
    }
}
```

### Option B: Loop and Try Start
Instead of probing with a dummy listener, modify `RhinoAIHost.TryGetNextPort` to actually loop and check if `McpServer.Start` succeeds. However, since `StartOrRestart` is what instantiates the server, the loop would need to happen in `StartOrRestart` or `OpenNewServer`. 
Given the current architecture, **Option A** is much less invasive and keeps the probing logic isolated inside `TryGetNextPort`.

## 4. Implementation Steps
1. Open `rhino/plugin/RhinoAIHost.ai.cs`.
2. Locate the `TryBindCandidate(int candidate, out int port)` method.
3. Replace the `TcpListener` logic with a dual-branch logic (Option A above):
   - If `candidate == 0`, keep the `TcpListener` logic to easily resolve the dynamic port.
   - If `candidate != 0`, probe using a temporary `HttpListener` bound to `http://localhost:{candidate}/`.
4. Compile the plugin and test opening a second Rhino instance to confirm it gracefully skips `10500` and successfully binds to `0` (or `10501` if a sequential loop is added). 
*(Note: Currently `TryGetNextPort` falls back to `0` if `10500` is busy, which is perfect and guarantees a free port for the second instance.)*
