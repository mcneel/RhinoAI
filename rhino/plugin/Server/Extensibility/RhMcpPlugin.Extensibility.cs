namespace RhMcp;

// The plug-in's half of the extensibility surface, kept out of Plugin.cs so that upstream
// file differs from McNeel's only by the `partial` keyword.
public partial class RhMcpPlugin
{
    // One instance for the process, because the registry behind it is process-global.
    private readonly Server.Extensibility.McpExtensionHost _extensionHost = new();

    /// <summary>
    /// The extension point other Rhino plug-ins use to contribute MCP tools to this
    /// server at run time.
    /// </summary>
    /// <returns>
    /// The <see cref="Server.Extensibility.McpExtensionHost"/> singleton. Callers reach it
    /// with <c>RhinoApp.GetPlugInObject(2668d7ed-f507-4a68-8295-8172147a0e39)</c> and use
    /// it by reflection, since there is no assembly for them to reference.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Available as soon as this plug-in is loaded, which is earlier than the MCP server
    /// itself starts -- that happens on the first document open. Registering before then is
    /// fine: the dispatcher reads the registry live on each request rather than caching a
    /// snapshot at start-up.
    /// </para>
    /// <para>
    /// Because <c>RhinoApp.GetPlugInObject</c> loads its target, a caller reaching for this
    /// is what loads this plug-in, so neither side needs the other to have loaded first.
    /// Callers should still do it from idle rather than their own <c>OnLoad</c>, to avoid a
    /// reentrant plug-in load inside Rhino's plug-in manager.
    /// </para>
    /// </remarks>
    public override object GetPlugInObject() => _extensionHost;
}
