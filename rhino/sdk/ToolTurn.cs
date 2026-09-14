using System.Collections.Generic;
using System.Text.Json;

namespace Rhino.AI;

public sealed record ToolTurn(string Name, List<KeyValuePair<string, string>> Args) : ITurn
{

    public bool Success { get; }

    private string? PrivateData { get; set; }
    public string Data
        => PrivateData ??= JsonSerializer.Serialize(new { Name, Args }) ?? string.Empty;

}
