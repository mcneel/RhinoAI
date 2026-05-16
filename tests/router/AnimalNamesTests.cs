using NUnit.Framework;

namespace RhMcp.Router.Tests;

// AnimalNames is a process-global counter, so every test must Reset() up front.
// Tests below assume the pool order in AnimalNames.cs — keep them in sync if
// the pool ever changes.
public class AnimalNamesTests
{
    // First few pool entries, in source order. Used to lock both the starting
    // point and ordering — full pool is 30 entries.
    private static readonly string[] ExpectedFirstFive =
    [
        "armadillo", "axolotl", "badger", "capybara", "cheetah",
    ];

    private const int PoolSize = 30;

    [SetUp]
    public void Setup() => AnimalNames.Reset();

    [Test]
    public void First_call_returns_first_pool_entry()
    {
        Assert.That(AnimalNames.Next(), Is.EqualTo("armadillo"));
    }

    [Test]
    public void Sequence_matches_pool_order_for_first_N()
    {
        var got = Enumerable.Range(0, ExpectedFirstFive.Length)
            .Select(_ => AnimalNames.Next())
            .ToArray();
        Assert.That(got, Is.EqualTo(ExpectedFirstFive));
    }

    [Test]
    public void Overflow_returns_slot_dash_index_with_pre_increment()
    {
        // After draining all 30 pool entries, _index == 30. The overflow branch
        // does `slot-{++_index}` (pre-increment), so the FIRST overflow name is
        // "slot-31", not "slot-30". Locking this behavior — if AnimalNames.cs
        // ever switches to post-increment, callers relying on uniqueness across
        // a Reset() would collide with the last pool entry's index.
        for (int i = 0; i < PoolSize; i++)
        {
            _ = AnimalNames.Next();
        }
        Assert.That(AnimalNames.Next(), Is.EqualTo("slot-31"));
        Assert.That(AnimalNames.Next(), Is.EqualTo("slot-32"));
    }

    [Test]
    public void Reset_restarts_the_sequence()
    {
        _ = AnimalNames.Next();
        _ = AnimalNames.Next();
        AnimalNames.Reset();
        Assert.That(AnimalNames.Next(), Is.EqualTo("armadillo"));
    }

    [Test]
    public void Parallel_calls_produce_distinct_names()
    {
        const int N = 100;
        var bag = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, N)
            .Select(_ => Task.Run(() => bag.Add(AnimalNames.Next())))
            .ToArray();
        Task.WaitAll(tasks);

        var distinct = bag.Distinct().ToArray();
        Assert.That(bag.Count, Is.EqualTo(N));
        Assert.That(distinct.Length, Is.EqualTo(N));
    }
}
