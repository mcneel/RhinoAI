namespace Rhino.AI.UI.Settings;

internal abstract record ServerOrigin
{
    private ServerOrigin() { }

    public sealed record BuiltIn : ServerOrigin;
    public sealed record PlugIn(PlugInItemViewModel Owner) : ServerOrigin;
    public sealed record Custom(IMcpDefinition Definition) : ServerOrigin;
}
