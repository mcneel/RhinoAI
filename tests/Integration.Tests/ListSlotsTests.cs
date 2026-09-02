using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// list_slots is the router's window into its own slot registry. With an
// isolated state dir and no user-started Rhino announcement to adopt, the
// router should report an empty list. Tests in this fixture do NOT spawn a
// Rhino — slot-population behaviour lives in SpawnSlotTests,
// GrasshopperStartTests, MultiRouterTests.
[TestFixture]
internal sealed class ListSlotsTests : SharedRouterFixture
{
    [Test]
    public async Task list_slots_is_empty_for_freshly_spawned_router()
    {
        ReturnResult result = await _router.CallToolAsync("list_slots");
        Assert.That(result.Payload?.GetArrayLength(), Is.EqualTo(0));
    }
}
