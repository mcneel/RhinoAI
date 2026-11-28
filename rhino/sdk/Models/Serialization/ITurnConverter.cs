using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>Translates a transcript to and from one vendor's wire format.</summary>
internal interface ITurnConverter
{

    public string Vendor { get; }

    public JsonObject ToRequest(RequestSettings settings, Harness harness, IEnumerable<ITurn> turns);

    public IReadOnlyList<ITurn> FromResponse(Harness harness, JsonNode response);

    /// <summary>
    /// Merges streaming events into one node to that can be converted into a series of ITurns
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when stream ends before finishing</exception>
    public JsonNode Merge(IEnumerable<JsonNode> events);

}

public readonly record struct RequestSettings(string Model, int MaxOutputTokens);
