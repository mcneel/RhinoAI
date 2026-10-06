using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>Translates a transcript to and from one vendor's wire format.</summary>
public interface ITurnConverter
{

    public string Vendor { get; }

    public JsonObject ToRequest(RequestSettings settings, IHarness harness, IEnumerable<ITurn> turns);

    public IReadOnlyList<ITurn> FromResponse(IHarness harness, JsonNode response);

}

public readonly record struct RequestSettings(string Model, int MaxOutputTokens);
