using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;

namespace Rhino.AI.Models;

internal abstract class ApiModel(string name, string vendor, Uri host, Protocol protocol) : IModel
{

    public string Name { get; } = name;

    public string Vendor { get; } = vendor;

    public int MaxOutputTokens { get; set; } = 8192;

    private Uri Host { get; } = host;

    private Protocol Protocol { get; } = protocol;

    private ITurnConverter Converter { get; } = protocol.Converter(vendor);

    protected HttpClient Http { get; } = new();

    public string? ApiKey { get; set; }

    public virtual async Task<IEnumerable<ITurn>> SendAsync(Harness harness, IEnumerable<ITurn> turns, CancellationToken token)
    {
        List<ITurn> turnsOut = [];
        await foreach (ITurn turn in StreamAsync(harness, turns, token).ConfigureAwait(false))
        {
            turnsOut.Add(turn);
        }

        return turnsOut;
    }

    public virtual async IAsyncEnumerable<ITurn> StreamAsync(Harness harness, IEnumerable<ITurn> turns, [EnumeratorCancellation] CancellationToken token)
    {
        JsonObject body = Converter.ToRequest(new RequestSettings(Name, MaxOutputTokens), harness, turns);
        // AI : Move to RequestSettings
        body["stream"] = true;
        body["stream_options"] = new JsonObject { ["include_usage"] = true };

        using HttpRequestMessage request = GetRequest(body);
        using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

        using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);

        using StreamReader reader = new(stream, Encoding.UTF8);

        List<JsonNode> events = [];

        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is string line)
        {
            if (line is null) continue;
            if (line.Length <= 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            
            string payload = line[5..].TrimStart();
            if (payload == "[DONE]") break;
            
            if (JsonNode.Parse(payload) is not JsonNode cast) continue;
            events.Add(cast);
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{Vendor} rejected the request ({(int)response.StatusCode})");

        JsonNode converted = Converter.Merge(events);

        foreach (ITurn turn in Converter.FromResponse(harness, converted))
            yield return turn;
    }

    private HttpRequestMessage GetRequest(JsonObject body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(Host, Protocol.Route(Name))) { Content = Payload(body) };
        if (ApiKey is string key)
            Protocol.Authorize(request.Headers, key);
        return request;
    }

    private static JsonSerializerOptions Wire { get; } = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static StringContent Payload(JsonObject body) => new(body.ToJsonString(Wire), Encoding.UTF8, "application/json");

}
