using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Rhino.AI;

public sealed record ToolTurn(string Name, List<IToolArg> Args, TimeSpan span = default, int tokenCount = 1) : ITurn
{

    public bool Success { get; }

    public int TokenCount { get; } = tokenCount;

    public TimeSpan Duration { get; } = span;

    private string? PrivateData { get; set; }
    public string Data
        => PrivateData ??= JsonSerializer.Serialize(new { Name, Args }) ?? string.Empty;

    public ITurn Copy() => new ToolTurn(Name, [.. Args], Duration, TokenCount);

}
