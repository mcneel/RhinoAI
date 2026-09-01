using NUnit.Framework;

namespace RhinoAI.Integration.Tests.Harness;

// Shared boilerplate for fixtures that need an isolated rh-mcp-router child
// process. Two flavours:
//   - RouterFixture: fresh router per test, for fixtures that mutate slot state.
//   - SharedRouterFixture: one router for the whole fixture, for read-only or
//     idempotent suites where the spawn cost matters.
// NUnit runs base [SetUp]/[OneTimeSetUp] before derived ones, so derived classes
// can add their own setup methods without overriding anything here.
public abstract class RouterFixture
{
    protected RhinoMcpRouter _router = null!;

    [SetUp]
    public async Task RouterFixtureSetUp()
    {
        _router = await RhinoMcpRouter.LaunchIsolatedAsync();
    }

    [TearDown]
    public async Task RouterFixtureTearDown()
    {
        if (_router is not null)
        {
            await _router.DisposeAsync();
        }
    }
}

public abstract class SharedRouterFixture
{
    protected RhinoMcpRouter _router = null!;

    [OneTimeSetUp]
    public async Task SharedRouterFixtureSetUp()
    {
        _router = await RhinoMcpRouter.LaunchIsolatedAsync();
    }

    [OneTimeTearDown]
    public async Task SharedRouterFixtureTearDown()
    {
        if (_router is not null)
        {
            await _router.DisposeAsync();
        }
    }
}
