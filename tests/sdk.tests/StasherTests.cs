using Rhino.AI.Secrets;

namespace sdk.tests;

public class StasherTests
{
    private string Key { get; set; } = string.Empty;

    [SetUp]
    public void SetUp()
    {
        Assume.That(Stasher.IsAvailable, "No OS vault on this machine.");
        Key = $"test-{Guid.NewGuid():N}";
    }

    [TearDown]
    public void TearDown() => Stasher.TryDeleteSecret(Key);

    [Test]
    public void RoundTrip()
    {
        Assert.That(Stasher.TrySetSecret(Key, "sk-first"));
        Assert.That(Stasher.TryGetSecret(Key, out string? secret));
        Assert.That(secret, Is.EqualTo("sk-first"));
    }

    [Test]
    public void Overwrite()
    {
        Assert.That(Stasher.TrySetSecret(Key, "sk-first"));
        Assert.That(Stasher.TrySetSecret(Key, "sk-second"));
        Assert.That(Stasher.TryGetSecret(Key, out string? secret));
        Assert.That(secret, Is.EqualTo("sk-second"));
    }

    [Test]
    public void Unicode()
    {
        Assert.That(Stasher.TrySetSecret(Key, "clé-🔑"));
        Assert.That(Stasher.TryGetSecret(Key, out string? secret));
        Assert.That(secret, Is.EqualTo("clé-🔑"));
    }

    [Test]
    public void Missing()
    {
        Assert.That(Stasher.TryGetSecret(Key, out string? secret), Is.False);
        Assert.That(secret, Is.Null);
    }

    [Test]
    public void Delete()
    {
        Assert.That(Stasher.TrySetSecret(Key, "sk-first"));
        Assert.That(Stasher.TryDeleteSecret(Key));
        Assert.That(Stasher.TryGetSecret(Key, out _), Is.False);
        Assert.That(Stasher.TryDeleteSecret(Key), Is.False);
    }

    [Test]
    public void RejectsEmpty()
    {
        Assert.That(Stasher.TrySetSecret(string.Empty, "sk-first"), Is.False);
        Assert.That(Stasher.TrySetSecret(Key, string.Empty), Is.False);
        Assert.That(Stasher.TryGetSecret(string.Empty, out _), Is.False);
    }
}
