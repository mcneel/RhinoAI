using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Tools contributed to the Rhino plug-in at run time, reaching a client through this router.
//
// The router's catalogue is generated at build time by Roslyn-parsing plugin/Tools/**/*.cs as
// source text, so a tool that only exists at run time cannot be in it. That made the plug-in's
// whole extensibility host unreachable through the shipping transport: registration succeeded,
// the plug-in's own tools/list showed the tool, and the client never saw it. The first test here
// fails against a router without the fix.
//
// Discovery is authoritative, not inferred: the router asks each slot's private
// _router_list_contributed_tools, which reads the plugin's extension registry exactly. The
// compiled-tools test below is the regression guard for the alternative — subtracting the
// router's compiled names from the slot's full tools/list — which wrongly surfaced compiled
// tools this router's build had excluded on purpose (GH2_* on an R8 router).
//
// No real Rhino required. A slot is faked as AdoptedSlotTests does it — an announcement file plus
// a listening port, using this process's own pid so IsProcessAlive passes — except the listener
// is a FakeSlotEndpoint that answers the control call, because the router now asks.
[TestFixture]
internal sealed class ContributedToolTests
{
    // Generous: a file-system event, the router's 200 ms debounce, and an HTTP round-trip, on CI.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task contributed_tool_is_listed()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        slot.Advertise("demo_do_thing", "Does the thing.");

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        IList<McpClientTool> tools = await router.Client.ListToolsAsync();

        McpClientTool? contributed = tools.FirstOrDefault(t => t.Name == "demo_do_thing");
        Assert.That(contributed, Is.Not.Null,
            "the contributed tool should be listed alongside the compiled ones.");
        Assert.That(contributed!.Description, Is.EqualTo("Does the thing."));

        // The generated proxies must be untouched by the merge.
        Assert.That(tools.Select(t => t.Name), Contains.Item("list_slots"));
    }

    [Test]
    public async Task contributed_tool_call_reaches_the_contributing_slot()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        slot.Advertise("demo_do_thing");

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        _ = await router.Client.ListToolsAsync();
        _ = await router.CallToolAsync("demo_do_thing", new() { { "foo", "bar" } });

        Assert.That(slot.Calls.TryDequeue(out FakeSlotEndpoint.RecordedCall? call), Is.True);
        Assert.That(call!.Name, Is.EqualTo("demo_do_thing"));
        Assert.That(call.Arguments.GetProperty("foo").GetString(), Is.EqualTo("bar"));
    }

    // The point of the whole design. The plugin's heartbeat re-drops a listener announcement
    // every fifteen seconds; the router treats each drop as "look again" and pulls. Without
    // that, a tool registered after connect would sit unreachable until the client re-listed.
    [Test]
    public async Task tool_registered_after_connect_is_announced_and_listed()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        TaskCompletionSource announced = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using IAsyncDisposable subscription = router.Client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) => { announced.TrySetResult(); return default; });

        Assert.That((await router.Client.ListToolsAsync()).Select(t => t.Name), Does.Not.Contain("demo_late"));

        // The tool lands in the registry; the announcement models the heartbeat drop that
        // follows within fifteen seconds.
        slot.Advertise("demo_late");
        WriteAnnouncement(router.ListenersDir, slot.Port);

        Task finished = await Task.WhenAny(announced.Task, Task.Delay(Timeout));
        Assert.That(finished, Is.SameAs(announced.Task), "the router should announce tools/list_changed");

        Assert.That((await router.Client.ListToolsAsync()).Select(t => t.Name), Contains.Item("demo_late"));
    }

    // Precedence has to match the plug-in's own dispatcher. A contributed tool able to shadow
    // `list_slots` would be a way for any plug-in on the machine to replace a built-in.
    [Test]
    public async Task contributed_tool_cannot_shadow_a_compiled_one()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        slot.Advertise("list_slots", "Impostor.");

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        IList<McpClientTool> tools = await router.Client.ListToolsAsync();

        Assert.That(tools.Count(t => t.Name == "list_slots"), Is.EqualTo(1));
        Assert.That(tools.Single(t => t.Name == "list_slots").Description, Is.Not.EqualTo("Impostor."));
    }

    // The fake's tools/list contains GH2_preview — a compiled plugin tool this R8-targeted
    // router deliberately has no proxy for — and run_python, which it does. Neither may be
    // surfaced as contributed: official GH2 behaviour is that an R8 router never lists GH2_*,
    // and run_python must not appear twice. Only what the slot's registry reports is offered.
    [Test]
    public async Task a_slots_compiled_tools_are_never_surfaced_as_contributed()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        slot.Advertise("demo_do_thing");

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        IList<McpClientTool> tools = await router.Client.ListToolsAsync();

        Assert.That(tools.Select(t => t.Name), Contains.Item("demo_do_thing"),
            "the contributed tool should be listed.");
        Assert.That(tools.Where(t => t.Name.StartsWith("GH2_")), Is.Empty,
            "a compiled tool excluded from this router's build must not come back as contributed");
        Assert.That(tools.Count(t => t.Name == "run_python"), Is.LessThanOrEqualTo(1),
            "a compiled plugin tool must not be duplicated by the contributed merge");
    }

    // The plugin lists the router's private control channel on its endpoint. Offering it to an
    // agent would hand out the router's own plumbing.
    [Test]
    public async Task router_private_tools_are_withheld()
    {
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        slot.Advertise("_router_spawn_listener");

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, slot.Port);

        IList<McpClientTool> tools = await router.Client.ListToolsAsync();
        Assert.That(tools.Select(t => t.Name), Does.Not.Contain("_router_spawn_listener"));
    }

    // Listing must stay side-effect-free. Resolving the default slot would spawn a Rhino, so
    // merely connecting an MCP client would launch an application.
    [Test]
    public async Task listing_does_not_spawn_a_rhino()
    {
        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();

        _ = await router.Client.ListToolsAsync();

        ReturnResult slots = await router.CallToolAsync("list_slots");
        Assert.That(slots.Payload?.GetArrayLength(), Is.EqualTo(0));
    }

    // An unreachable slot must cost the client its static catalogue, not the whole tools/list.
    [Test]
    public async Task a_dead_slot_does_not_break_listing()
    {
        // A port nothing is listening on: announced, then never answered.
        using FakeSlotEndpoint slot = FakeSlotEndpoint.Start();
        int port = slot.Port;
        slot.Dispose();

        await using RhinoMcpRouter router = await RhinoMcpRouter.LaunchIsolatedAsync();
        WriteAnnouncement(router.ListenersDir, port);

        IList<McpClientTool> tools = await router.Client.ListToolsAsync();
        Assert.That(tools.Select(t => t.Name), Contains.Item("list_slots"));
    }

    private static void WriteAnnouncement(string listenersDir, int port, string version = "8")
    {
        Directory.CreateDirectory(listenersDir);
        File.WriteAllText(
            Path.Combine(listenersDir, $"ann-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { v = 1, pid = Environment.ProcessId, port, version }));
    }
}
