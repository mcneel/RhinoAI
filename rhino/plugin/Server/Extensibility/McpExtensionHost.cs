using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Server.Extensibility;

/// <summary>
/// The object other Rhino plug-ins get from <c>RhinoApp.GetPlugInObject(rhinoMcpPlugInId)</c>.
/// It lets them contribute MCP tools to this server at run time.
/// </summary>
/// <remarks>
/// <para>
/// Everything crossing this boundary is either a <see cref="string"/> or a delegate built
/// from corelib types. That is deliberate and load-bearing: this plug-in is built with
/// <c>EnableDynamicLoading=true</c>, so it loads into its own <c>AssemblyLoadContext</c> with
/// its dependencies copied beside the .rhp. A caller in the default context would bind a
/// *different* <c>System.Text.Json</c>, so a <c>JsonElement</c> passed here would be a
/// different type with the same name -- the cast would fail silently and the tool would
/// simply never appear. <c>string</c>, <c>Func&lt;&gt;</c>, <c>Task&lt;&gt;</c> and
/// <c>CancellationToken</c> all live in System.Private.CoreLib, which can never be loaded
/// twice, so they are the same type everywhere.
/// </para>
/// <para>
/// Callers must not call this from their plug-in's <c>OnLoad</c>: reaching for another
/// plug-in's object during load is a reentrant plug-in load inside Rhino's plug-in manager.
/// Defer to a one-shot <c>RhinoApp.Idle</c> handler.
/// </para>
/// <para>Typical use, from a contributing plug-in (no reference to this assembly):</para>
/// <code>
/// object host = RhinoApp.GetPlugInObject(new Guid("2668d7ed-f507-4a68-8295-8172147a0e39"));
/// MethodInfo register = host.GetType().GetMethod("RegisterMcpTool");
/// Func&lt;string, CancellationToken, Task&lt;string&gt;&gt; handler = MyToolAsync;
/// string error = (string)register.Invoke(host, new object[] { descriptorJson, handler });
/// </code>
/// </remarks>
public sealed class McpExtensionHost
{
    /// <summary>
    /// Contract version. A caller should check this before registering.
    /// </summary>
    public int McpExtensionProtocol => ExtensionProtocol.Version;

    /// <summary>
    /// Removes a previously registered tool. Returns true if it was there.
    /// </summary>
    public bool UnregisterMcpTool(string name) => McpExtensionRegistry.Current.Unregister(name);

    /// <summary>
    /// Removes every tool registered under the given <c>owner</c>.
    /// </summary>
    public int UnregisterMcpToolsByOwner(string owner) => McpExtensionRegistry.Current.UnregisterByOwner(owner);

    /// <summary>
    /// The names of every tool currently contributed by other plug-ins.
    /// </summary>
    public string[] RegisteredMcpToolNames() => McpExtensionRegistry.Current.Names();

    /// <summary>
    /// Removes the transformer registered by <paramref name="owner"/>.
    /// </summary>
    public bool UnregisterMcpResultTransform(string owner) =>
        McpExtensionRegistry.Current.UnregisterTransform(owner);

    /// <summary>
    /// Registers one tool. <paramref name="descriptorJson"/> describes it:
    /// <code>
    /// { "owner": "com.example.myplugin",       // required, reverse-DNS, groups your tools
    ///   "name": "my_tool",                     // required, unique across the server
    ///   "title": "My Tool",                    // optional
    ///   "description": "What it does.",        // required -- an LLM cannot use an undescribed tool
    ///   "inputSchema": { "type": "object", "properties": { }, "required": [] },
    ///   "annotations": { "readOnlyHint": true, "destructiveHint": false },
    ///   "requiresUiThread": false }            // optional, see below
    /// </code>
    /// <paramref name="handler"/> receives the call's arguments as a JSON object string and
    /// returns either an MCP result object
    /// (<c>{"content":[{"type":"text","text":"…"}],"isError":false}</c>) or any other string,
    /// which is wrapped as a single text block.
    /// </summary>
    /// <returns>An empty string on success, otherwise the reason it was rejected.</returns>
    /// <remarks>
    /// The handler runs on a background thread by default -- the inverse of the default for
    /// tools compiled into this plug-in. A contributing plug-in does its own UI-thread
    /// marshalling and knows what it touches, and its work may run for minutes; holding the
    /// Rhino message pump for that would freeze the application. Set
    /// <c>"requiresUiThread": true</c> to opt in to marshalling, which covers only the
    /// synchronous prefix of an async handler.
    /// </remarks>
    public string RegisterMcpTool(string descriptorJson, Func<string, CancellationToken, Task<string>> handler)
    {
        if (handler is null)
            return "handler is null";

        if (!ProviderToolDescriptor.TryParse(descriptorJson, out ProviderToolDescriptor descriptor, out string failure))
            return failure;

        return McpExtensionRegistry.Current.Register(descriptor, handler);
    }

 
    /// <summary>
    /// Registers a transformer that may rewrite the result of <em>any</em> tool call this
    /// server serves, including tools compiled into this plug-in. It receives a context JSON
    /// object (<c>{ "tool", "arguments", "endpoint", "source", "owner" }</c>) and the result
    /// JSON, and returns a replacement result -- or null, empty, or the input unchanged to
    /// decline.
    /// </summary>
    /// <remarks>
    /// Transformers run in ascending <paramref name="order"/>, ties broken by owner. A
    /// transformer that throws, hangs, or returns unusable JSON is skipped and the previous
    /// result is kept: a decorator must never be able to fail a tool call that succeeded.
    /// </remarks>
    /// <returns>An empty string on success, otherwise the reason it was rejected.</returns>
    public string RegisterMcpResultTransform(
        string owner, int order, Func<string, string, CancellationToken, Task<string>> transform)
    {
        if (transform is null)
            return "transform is null";

        if (string.IsNullOrWhiteSpace(owner))
            return "owner is required";

        return McpExtensionRegistry.Current.RegisterTransform(owner, order, transform);
    }
}
