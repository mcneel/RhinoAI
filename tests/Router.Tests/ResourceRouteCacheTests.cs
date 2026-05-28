using RhMcp.Router.Resources;
using Xunit;

namespace RhMcp.Router.Tests;

// Tests the URI → slot cache that ResourceProxy populates during list and
// consults during read. The cache replaces wholesale on every list call, so
// the contract is straightforward: whatever the last Replace saw is the
// snapshot; Invalidate drops entries pointing at a given slot.
public class ResourceRouteCacheTests
{
    [Fact]
    public void Replace_overwrites_previous_entries()
    {
        using ResourceRouteCache cache = new();
        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://a", "slot-1"),
            new KeyValuePair<string, string>("rhino://b", "slot-1"),
        });

        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://c", "slot-2"),
        });

        Assert.False(cache.TryGetSlotId("rhino://a", out _));
        Assert.False(cache.TryGetSlotId("rhino://b", out _));
        Assert.True(cache.TryGetSlotId("rhino://c", out string slot));
        Assert.Equal("slot-2", slot);
    }

    [Fact]
    public void TryGetSlotId_returns_false_for_unknown_uri()
    {
        using ResourceRouteCache cache = new();
        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://a", "slot-1"),
        });

        Assert.False(cache.TryGetSlotId("rhino://nope", out string slot));
        Assert.Null(slot);
    }

    [Fact]
    public void Invalidate_drops_entries_for_slot_only()
    {
        using ResourceRouteCache cache = new();
        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://a", "slot-1"),
            new KeyValuePair<string, string>("rhino://b", "slot-2"),
            new KeyValuePair<string, string>("rhino://c", "slot-1"),
        });

        cache.Invalidate("slot-1");

        Assert.False(cache.TryGetSlotId("rhino://a", out _));
        Assert.False(cache.TryGetSlotId("rhino://c", out _));
        Assert.True(cache.TryGetSlotId("rhino://b", out string slot));
        Assert.Equal("slot-2", slot);
    }

    [Fact]
    public void Snapshot_returns_independent_copy()
    {
        using ResourceRouteCache cache = new();
        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://a", "slot-1"),
        });

        IReadOnlyDictionary<string, string> snapshot = cache.Snapshot();
        cache.Replace(new[]
        {
            new KeyValuePair<string, string>("rhino://b", "slot-2"),
        });

        // Snapshot taken before the second Replace must still see the old state.
        Assert.Single(snapshot);
        Assert.Equal("slot-1", snapshot["rhino://a"]);
    }

    [Fact]
    public async Task Concurrent_readers_and_writer_do_not_throw()
    {
        // Smoke test for the reader-writer lock — a real race condition would
        // typically surface as an InvalidOperationException from a torn dict.
        using ResourceRouteCache cache = new();
        cache.Replace(new[] { new KeyValuePair<string, string>("rhino://a", "slot-1") });

        CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        Task readers = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                cache.TryGetSlotId("rhino://a", out _);
                _ = cache.Snapshot();
            }
        });

        Task writer = Task.Run(() =>
        {
            int n = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                cache.Replace(new[]
                {
                    new KeyValuePair<string, string>($"rhino://k{n++}", $"slot-{n % 4}"),
                });
            }
        });

        await Task.WhenAll(readers, writer);
    }
}
