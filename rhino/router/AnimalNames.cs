namespace RhMcp.Router;

// Hand-picked list of memorable, short, distinct animal names for slot IDs.
// Picked in order; once exhausted, falls back to numbered "slot-N".
public static class AnimalNames
{
    private static readonly string[] _pool =
    [
        "armadillo", "axolotl",   "badger",   "capybara", "cheetah",
        "echidna",   "falcon",    "fennec",   "gecko",    "ibex",
        "kakapo",    "kinkajou",  "koala",    "lemur",    "magpie",
        "manatee",   "marmot",    "narwhal",  "ocelot",   "okapi",
        "otter",     "panda",     "pangolin", "platypus", "puffin",
        "quokka",    "sifaka",    "tapir",    "tarsier",  "wombat"
    ];

    private static int _index = 0;
    private static readonly object _lock = new();

    public static string Next()
    {
        lock (_lock)
        {
            if (_index < _pool.Length)
            {
                return _pool[_index++];
            }
            return $"slot-{++_index}";
        }
    }

    public static void Reset()
    {
        lock (_lock) _index = 0;
    }
}
