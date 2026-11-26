namespace Rhino.AI.UI.Settings;

internal enum Brand { Anthropic, DeepSeek, Google, OpenAI }

internal abstract record SeriesHue
{
    public const int PoolSize = 20;

    private SeriesHue() { }

    public sealed record OfBrand(Brand Brand) : SeriesHue;

    public sealed record Pooled(int Slot) : SeriesHue;

    public static SeriesHue Of(string? vendor, string seriesId) => vendor?.ToLowerInvariant() switch
    {
        "anthropic" => new OfBrand(Brand.Anthropic),
        "deepseek" => new OfBrand(Brand.DeepSeek),
        "google" => new OfBrand(Brand.Google),
        "openai" => new OfBrand(Brand.OpenAI),
        null or "custom" => new Pooled(PoolSlotOf(seriesId)),
        string named => new Pooled(PoolSlotOf(named)),
    };

    // string.GetHashCode is randomised per process, which would repaint pooled series on every launch.
    private static int PoolSlotOf(string key)
    {
        uint hash = 2166136261;
        foreach (char c in key)
            hash = (hash ^ c) * 16777619;
        return (int)(hash % PoolSize);
    }
}

internal readonly record struct SeriesColour(SeriesHue Hue, int Shade);
