namespace Rhino.AI.Server.Tests;

internal sealed class StubServices(params object[] services) : IServiceProvider
{
    private object[] Services { get; } = services;

    public object? GetService(Type serviceType) =>
        Services.FirstOrDefault(serviceType.IsInstanceOfType);
}
