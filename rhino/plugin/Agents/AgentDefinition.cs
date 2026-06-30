using System.Collections.Generic;

namespace RhMcp;

// Dumb, immutable, behavior-free description of an agent. Built via `new(...)` or `with`
// expressions. Model/SystemPrompt are empty strings (never null); SearchPaths/ExtraArgs are
// non-null lists. Discovery (Available) is computed by the registry, not stored here.
internal sealed record AgentDefinition(
    string Name,
    AgentAdapter Adapter,
    string Command,
    IReadOnlyList<string> SearchPaths,
    string Model,
    IReadOnlyList<string> ExtraArgs,
    string SystemPrompt,
    bool Enabled,
    bool IsBuiltin);
