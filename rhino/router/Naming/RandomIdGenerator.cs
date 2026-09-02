using System;

namespace RhinoAI.Router;

/// <summary>
/// Generates human-memorable IDs of the form adjective-biome-verb,
/// e.g. "stormy-sea-howls".
/// </summary>
public static class RandomIdGenerator
{

    public static string NewId()
    {
        string adj = Adjectives[Rng.Next(Adjectives.Length)];
        string biome = Biomes[Rng.Next(Biomes.Length)];
        string verb = Verbs[Rng.Next(Verbs.Length)];
        return $"{adj}-{biome}-{verb}";
    }
    
    private static Random Rng { get; } = new();

    private static string[] Adjectives { get; } =
    [
        "stormy",     "thundery",   "windy",      "gusty",      "blustery",
        "breezy",     "squally",    "tempestuous","turbulent",  "raging",
        "howling",    "roaring",    "swirling",   "whirling",   "drifting",
        "billowing",  "sweeping",   "blowing",    "windswept",  "galeblown",
        "rainy",      "drizzly",    "showery",    "soggy",      "sodden",
        "drenched",   "dripping",   "damp",       "dewy",       "sopping",
        "splashing",  "moist",      "humid",      "sultry",     "muggy",
        "misty",      "foggy",      "hazy",       "cloudy",     "overcast",
        "murky",      "dim",        "gloomy",     "dreary",     "bleak",
        "somber",     "dusky",      "shadowy",    "shaded",     "dappled",
        "smoky",      "smoggy",     "vaporous",   "steaming",   "steamy",
        "smoldering", "ashen",      "sooty",      "dusty",      "smouldery",
        "frosty",     "icy",        "snowy",      "chilly",     "freezing",
        "frigid",     "wintry",     "glacial",    "arctic",     "polar",
        "frozen",     "sleety",     "slushy",     "crystalline","glassy",
        "hoary",      "rimed",      "subzero",    "boreal",     "alpine",
        "sunny",      "scorching",  "sweltering", "blazing",    "blistering",
        "searing",    "torrid",     "tropical",   "sizzling",   "ablaze",
        "fiery",      "baking",     "sunlit",     "sunbaked",   "sunwashed",
        "balmy",      "mild",       "temperate",  "pleasant",   "fair",
        "calm",       "still",      "quiet",      "tranquil",   "gentle",
        "placid",     "serene",     "peaceful",   "hushed",     "silent",
        "becalmed",   "lulled",
        "bright",     "radiant",    "glowing",    "gleaming",   "shining",
        "shimmering", "sparkling",  "glittering", "glinting",   "lustrous",
        "dazzling",   "brilliant",  "beaming",    "luminous",   "incandescent",
        "starry",     "moonlit",    "twilit",     "dawnlit",    "auroral",
        "nebulous",   "cloudless",  "clear",      "pristine",
        "crisp",      "fresh",      "brisk",      "bracing",    "biting",
        "nipping",    "cutting",    "sharp",      "raw",        "keen",
        "parched",    "scorched",   "withered",   "arid",       "dry",
        "golden",     "silver",     "amber",      "crimson",    "scarlet",
        "azure",      "rosy",       "pearly",     "opalescent", "iridescent",
        "echoing",    "whispering", "murmuring",  "rustling",   "sighing",
        "moaning",    "groaning",   "creaking",   "rumbling",
        "fogbound",   "snowbound",  "icebound",   "rainswept",  "stormbound",
        "boiling",    "simmering",  "bubbling",   "frothing",   "churning",
        "ethereal",   "airy",       "gauzy",      "wispy",      "feathery",
        "volcanic",   "seismic",    "thundering", "electric",   "charged",
        "muddy",      "silty",      "brackish",   "stagnant",   "briny",
        "salty",      "tidal",      "wavetossed", "stormtossed","seasprayed",
        "tempestlashed","windborne","cloudbound", "sunbleached","weathered"
    ];

    private static string[] Biomes { get; } =
    [
        "sea",         "ocean",       "lake",        "pond",        "river",
        "stream",      "brook",       "creek",       "lagoon",      "bay",
        "gulf",        "fjord",       "estuary",     "delta",       "tarn",
        "mere",        "loch",        "bayou",       "slough",      "oxbow",
        "marsh",       "swamp",       "bog",         "fen",         "mire",
        "wetland",     "mangrove",    "floodplain",  "mudflat",     "saltmarsh",
        "reef",        "atoll",       "shoal",       "strait",      "sound",
        "cove",        "inlet",       "harbor",      "tidepool",    "sandbar",
        "geyser",      "spring",      "waterfall",   "cascade",     "rapids",
        "oasis",       "billabong",   "channel",     "sluice",      "hotspring",
        "forest",      "jungle",      "rainforest",  "woodland",    "grove",
        "thicket",     "copse",       "glade",       "taiga",       "hedgerow",
        "orchard",     "brushland",   "canopy",      "undergrowth", "treeline",
        "tundra",      "glacier",     "iceberg",     "icecap",      "icefield",
        "permafrost",  "snowfield",   "snowdrift",   "icefloe",     "icepack",
        "desert",      "dunes",       "badlands",    "savanna",     "steppe",
        "prairie",     "plains",      "mesa",        "butte",       "canyon",
        "plateau",     "scrubland",   "chaparral",   "veldt",       "outback",
        "wasteland",   "saltflat",    "playa",       "salina",      "drylake",
        "mountain",    "peak",        "summit",      "ridge",       "cliff",
        "crag",        "bluff",       "slope",       "valley",      "vale",
        "gorge",       "ravine",      "gully",       "foothills",   "highlands",
        "lowlands",    "moors",       "heath",       "fell",        "knoll",
        "hill",        "hillside",    "hollow",      "dell",        "glen",
        "volcano",     "caldera",     "crater",      "lavafield",   "fumarole",
        "meadow",      "pasture",     "field",       "paddock",     "lea",
        "basin",       "flatland",    "barrens",
        "island",      "isle",        "peninsula",   "archipelago", "cape",
        "headland",    "coast",       "shore",       "beach",       "strand",
        "foreshore",   "shoreline",   "coastline",   "ridgeline",
        "cavern",      "cave",        "grotto",      "sinkhole",    "abyss",
        "chasm",       "crevasse",    "gulch",       "fissure",
        "seabed",      "trench",      "shelf",       "kelpforest",  "seamount",
        "cloudbank",   "horizon",     "skyline",     "watershed",   "headwater",
        "tributary",   "confluence",  "ford",        "shallows",    "deeps"
    ];

    private static string[] Verbs { get; } =
    [
        "prowls",   "dances",   "leaps",    "stalks",   "drifts",
        "soars",    "hunts",    "naps",     "sings",    "hums",
        "growls",   "purrs",    "barks",    "howls",    "whispers",
        "shouts",   "tumbles",  "glides",   "creeps",   "darts",
        "marches",  "wanders",  "rests",    "dreams",   "ponders",
        "watches",  "listens",  "broods",   "skips",    "hops",
        "trots",    "gallops",  "swims",    "dives",    "climbs",
        "burrows",  "perches",  "roosts",   "scurries", "lumbers",
        "saunters", "frolics",  "twirls",   "splashes", "wiggles",
        "stomps",   "tiptoes",  "grumbles", "yawns",    "chirps",
        "strolls",  "ambles",   "scampers", "pounces",  "sprints",
        "races",    "jogs",     "hikes",    "strides",  "paces",
        "rambles",  "roams",    "prances",  "capers",   "bounds",
        "jumps",    "vaults",   "hurdles",  "lunges",   "spins",
        "whirls",   "swirls",   "floats",   "hovers",   "swoops",
        "dips",     "plunges",  "plummets", "cascades", "trickles",
        "flows",    "streams",  "rushes",   "surges",   "gushes",
        "ripples",  "bubbles",  "boils",    "simmers",  "sizzles",
        "crackles", "pops",     "fizzles",  "fizzes",   "hisses",
        "wheezes",  "puffs",    "pants",    "sighs",    "gasps",
        "mutters",  "mumbles",  "babbles",  "chatters", "prattles",
        "gossips",  "quibbles", "quarrels", "squabbles","bickers",
        "jests",    "jokes",    "teases",   "chuckles", "giggles",
        "snickers", "chortles", "cackles",  "guffaws",  "snorts",
        "sneezes",  "sniffles", "sniffs",   "wheedles", "cajoles",
        "coaxes",   "pleads",   "begs",     "implores", "entreats",
        "thanks",   "praises",  "cheers",   "applauds", "claps",
        "hails",    "greets",   "waves",    "nods",     "bows",
        "kneels",   "squats",   "crouches", "huddles",  "cuddles",
        "snuggles", "nestles",  "nests",    "settles",  "lounges",
        "sprawls",  "reclines", "dozes",    "slumbers", "snores",
        "wakes",    "stretches","flexes",   "limbers",  "warms",
        "cools",    "freezes",  "melts",    "thaws",    "shrinks",
        "grows",    "sprouts",  "blooms",   "blossoms", "wilts",
        "fades",    "ages",     "ripens",   "mellows",  "brightens",
        "shines",   "sparkles", "shimmers", "glitters", "gleams",
        "glows",    "flickers", "flashes",  "blinks",   "winks",
        "squints",  "peeks",    "peers",    "gazes",    "stares",
        "glares",   "ogles",    "spies",    "scouts",   "patrols",
        "guards",   "lingers",  "loiters",  "dawdles",  "idles"
    ];

}
