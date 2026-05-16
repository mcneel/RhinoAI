namespace RhMcp.Router;

// Hand-picked list of memorable, short, distinct animal names for slot IDs.
// Names are seeded into SlotStore's name_pool table on first router startup;
// allocation happens inside the spawn transaction so concurrent routers can't
// claim the same name. Pool order here defines first-seed idx; once persisted
// in the DB it's the DB row that wins.
public static class AnimalNames
{
    public static string[] Pool { get; } =
    [
        "aardvark",  "armadillo", "axolotl",  "badger",   "bonobo",
        "capybara",  "caracal",   "cheetah",  "coati",    "dingo",
        "dugong",    "echidna",   "falcon",   "fennec",   "ferret",
        "gecko",     "gibbon",    "hedgehog", "ibex",     "jerboa",
        "kakapo",    "kinkajou",  "koala",    "lemur",    "llama",
        "macaw",     "magpie",    "manatee",  "marmot",   "meerkat",
        "mongoose",  "narwhal",   "numbat",   "ocelot",   "okapi",
        "otter",     "panda",     "pangolin", "pika",     "platypus",
        "puffin",    "quokka",    "quoll",    "raccoon",  "serval",
        "sifaka",    "tapir",     "tarsier",  "vicuna",   "wombat"
    ];
}
