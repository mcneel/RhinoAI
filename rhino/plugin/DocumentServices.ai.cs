namespace Rhino.AI;

internal sealed class DocumentServices(RhinoDoc doc) : IServiceProvider
{
    private RhinoDoc Doc { get; } = doc;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(RhinoDoc) ? Doc : null;
}
