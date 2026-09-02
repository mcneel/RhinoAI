using System.Text.Json;

using RhinoAI.Integration.Tests.Harness;

namespace RhinoAI.Integration.Tests;

// Tests that need a live Rhino with the rh-mcp plugin loaded. The fixture
// spawns Rhino via the router's spawn_slot, so Rhino must be installed and
// licensed, and the freshly-built plugin must be loadable.
[TestFixture]
internal sealed class PluginToolTests : SharedRouterFixture
{
    private string _slot = null!;

    // Base sets up the router; we then spawn a single slot shared across every
    // plugin-side test in the fixture.
    [OneTimeSetUp]
    public async Task SpawnSharedSlot()
    {
        ReturnResult spawn = await _router.CallToolAsync("spawn_slot");
        if (spawn.Error is not null)
        {
            Assert.Inconclusive(
                $"spawn_slot failed; cannot run plugin-side tests. Error: {spawn.Error.Code}: {spawn.Error.Message}");
        }
        _slot = spawn.Payload!.Value.GetProperty("slotId").GetString()!;
    }

    // Runs before the base disposes the router.
    [OneTimeTearDown]
    public async Task CloseSharedSlot()
    {
        if (_slot is not null)
        {
            try
            {
                await _router.CallToolTextAsync("close_slot", Args.Of(("slot", _slot)));
            }
            catch { /* best effort */ }
        }
    }

    // Regression: a previous version of the filter stripped leading '_' / '-'
    // via TrimStart, which for the literal filter "_" produced an empty needle
    // and returned every registered command. The fix falls back to the
    // original filter when trimming empties it. Rhino command names never
    // contain '_', so a filter of "_" must return zero commands.
    [Test]
    public async Task get_commands_with_underscore_filter_does_not_return_every_command()
    {
        ReturnResult result = await _router.CallToolAsync(
            "get_commands",
            Args.Of(("slot", (object?)_slot), ("filter", "_")));

        string? text = result.Payload?.GetString();
        Assert.That(text, Does.Contain("No commands found"),
            $"Expected no matches for filter '_'; got:\n{text}");
    }

    // Sanity check that the trim-when-meaningful path still works. "_Box"
    // should be normalised to "Box" and find at least the Box command.
    [Test]
    public async Task get_commands_strips_leading_underscore_in_user_filter()
    {
        ReturnResult result = await _router.CallToolAsync(
            "get_commands",
            Args.Of(("slot", (object?)_slot), ("filter", "_Box")));

        string? text = result.Payload?.GetString();
        Assert.That(text, Does.Contain("Box"));
        Assert.That(text, Does.Not.StartWith("No commands found"));
    }

    // Regression: when a caller supplies a non-existent layer as the only
    // filter, the layer-index filter never gets set on ObjectEnumeratorSettings,
    // and the enumerator yields every object in the document. The fix
    // short-circuits the enumeration when the layer didn't resolve.
    [Test]
    public async Task set_selection_with_unresolved_layer_only_selects_nothing()
    {
        // Create a few objects so a select-all bug would be visible.
        await _router.CallToolTextAsync("run_python", Args.Of(
            ("slot", (object?)_slot),
            ("script", """
                from Rhino.Geometry import Point3d, Line
                doc = __rhino_doc__
                for i in range(3):
                    doc.Objects.AddLine(Line(Point3d(i, 0, 0), Point3d(i, 1, 0)))
                """)));

        ReturnResult result = await _router.CallToolAsync("set_selection", Args.Of(
            ("slot", (object?)_slot),
            ("layer", "this-layer-does-not-exist")));

        string? text = result.Payload?.GetString();
        Assert.That(text, Does.Contain("Selected 0 object(s)"));
        Assert.That(text, Does.Contain("Layer not found"));
    }

    // Regression: the script tools previously joined captured lines with "\n",
    // which produced doubled newlines because Rhino's CapturedCommandWindowStrings
    // already terminates each entry with '\n'. The fix concatenates without
    // injecting a separator. This test asserts the output between two prints
    // is a single newline rather than the legacy "\n\n".
    [Test]
    public async Task run_python_does_not_double_newlines_between_print_lines()
    {
        ReturnResult result = await _router.CallToolAsync("run_python", Args.Of(
            ("slot", (object?)_slot),
            ("script", "print(\"first\")\nprint(\"second\")")));

        string? stdout = result.Payload?.GetProperty("stdout").GetString();
        Assert.That(stdout, Does.Contain("first"));
        Assert.That(stdout, Does.Contain("second"));
        Assert.That(stdout, Does.Not.Contain("first\n\nsecond"));
    }

    private const string ScratchPlugin = "RhinoAIIntegrationTests";

    private async Task<JsonElement?> ManageCommand(string action, string name, string? script = null)
    {
        (string, object?)[] args = script is null
            ? [("slot", _slot), ("action", action), ("name", name), ("plugin", ScratchPlugin)]
            : [("slot", _slot), ("action", action), ("name", name), ("script", script), ("plugin", ScratchPlugin)];

        ReturnResult result = await _router.CallToolAsync("manage_plugin_commands", Args.Of(args));
        return result.Payload;
    }

    [Test]
    public async Task manage_plugin_commands_rejects_a_name_with_spaces()
    {
        JsonElement? payload = await ManageCommand("add", "Make It Blue", "print('hi')");

        Assert.That(payload?.GetProperty("error").GetString(), Does.Contain("not valid"));
    }

    [Test]
    public async Task manage_plugin_commands_rejects_an_unknown_action()
    {
        JsonElement? payload = await ManageCommand("create", "McpScratch", "print('hi')");

        Assert.That(payload?.GetProperty("error").GetString(), Does.Contain("Unknown action"));
    }

    [Test]
    public async Task manage_plugin_commands_requires_a_script_to_add()
    {
        JsonElement? payload = await ManageCommand("add", "McpScratch");

        Assert.That(payload?.GetProperty("error").GetString(), Does.Contain("Python script is required"));
    }

    // Builds and hot-loads a real plugin, so it is the slowest test here by a wide margin.
    [Test]
    public async Task manage_plugin_commands_adds_updates_and_deletes_a_command()
    {
        const string command = "McpScratchHello";
        await ManageCommand("delete", command);

        JsonElement? added = await ManageCommand("add", command, "print('scratch one')");
        Assert.That(added?.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null),
            $"add failed: {added?.GetProperty("error")}");
        Assert.That(added?.GetProperty("built").GetBoolean(), Is.True);
        Assert.That(added?.GetProperty("loaded").GetBoolean(), Is.True);

        ReturnResult ran = await _router.CallToolAsync("run_command", Args.Of(
            ("slot", (object?)_slot), ("command", command)));
        Assert.That(ran.Error, Is.Null);

        JsonElement? updated = await ManageCommand("update", command, "print('scratch two')");
        Assert.That(updated?.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null),
            $"update failed: {updated?.GetProperty("error")}");
        Assert.That(updated?.GetProperty("built").GetBoolean(), Is.True);

        JsonElement? deleted = await ManageCommand("delete", command);
        Assert.That(deleted?.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null),
            $"delete failed: {deleted?.GetProperty("error")}");
        Assert.That(
            deleted?.GetProperty("commands").EnumerateArray().Select(c => c.GetString()),
            Does.Not.Contain(command));
    }
}
