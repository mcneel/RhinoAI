using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed record AgentsContext(HostOs Os, bool InternetAllowed, Func<string?, KeySource> KeySourceOf);
