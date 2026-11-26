namespace Rhino.AI.UI.Settings;

internal enum AgentChange { Add, Remove }

internal enum AgentFailure { NotFound, Unsupported, Failed }

internal sealed record AgentProblem(AgentChange Change, AgentFailure Failure);
