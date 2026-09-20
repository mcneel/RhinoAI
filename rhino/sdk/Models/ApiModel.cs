using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal abstract class ApiModel(string name, string vendor, ITurnConverter converter) : IModel
{

    public string Name { get; } = name;
    public string Vendor { get; } = vendor;

    public int MaxOutputTokens { get; set; } = 8192;

    protected ITurnConverter Converter { get; } = converter;

    protected HttpClient Http { get; } = new();

    protected string? ApiKey { get; set; }

    protected string Key => ApiKey ?? throw new InvalidOperationException($"No API key is configured for {Vendor}.");

    public virtual async Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
    {
        if (!UserPermissions.IsPermitted(Vendor, Name))
            throw new PermissionException("Model or Vendor does not have permission.");

        JsonObject body = Converter.ToRequest(new RequestSettings(Name, MaxOutputTokens), harness, turns);

        using HttpRequestMessage request = GetRequest(body);
        using HttpResponseMessage response = await Http.SendAsync(request, token);

        string json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{Vendor} rejected the request ({(int)response.StatusCode}): {json}");

        JsonNode node = JsonNode.Parse(json) ?? throw new HttpRequestException($"{Vendor} returned an empty body.");

        return Converter.FromResponse(harness, node);
    }

    protected abstract HttpRequestMessage GetRequest(JsonObject body);

    private static JsonSerializerOptions Wire { get; } = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    protected static StringContent Payload(JsonObject body) => new(body.ToJsonString(Wire), Encoding.UTF8, "application/json");

}
