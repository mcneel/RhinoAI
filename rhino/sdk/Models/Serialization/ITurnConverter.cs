using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>Translates a transcript to and from one vendor's wire format.</summary>
internal interface ITurnConverter
{

    public string Vendor { get; }

    public JsonObject ToRequest(RequestSettings settings, Harness harness, IEnumerable<ITurn> turns);

    public IReadOnlyList<ITurn> FromResponse(Harness harness, JsonNode response);

}

public readonly record struct RequestSettings(string Model, int MaxOutputTokens);
