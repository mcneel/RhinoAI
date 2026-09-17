namespace Rhino.AI;

internal sealed class DocumentServices(RhinoDoc doc) : IServiceProvider
{
    private RhinoDoc Doc { get; } = doc;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(RhinoDoc) ? Doc : null;
}

// For a server that belongs to Rhino rather than to one document: the document is resolved per
// request, so the assistant's tools act on whatever is in front at the moment of the call — which
// is what its panel does too.
internal sealed class ActiveDocumentServices : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        serviceType == typeof(RhinoDoc) ? RhinoDoc.ActiveDoc : null;
}
