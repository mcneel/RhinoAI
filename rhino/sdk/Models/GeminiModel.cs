using System.Net.Http;
using System.Text.Json.Nodes;

namespace Rhino.AI.Models;

// https://ai.google.dev/gemini-api/docs#rest

internal sealed class GeminiModel : ApiModel
{

    private const string Host = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiModel(string name, string vendor) : base(name, vendor, new GeminiSerializationConverter())
    {
    }

    protected override HttpRequestMessage GetRequest(JsonObject body) => new(HttpMethod.Post, $"{Host}/{Name}:generateContent")
    {
        Headers = { { "x-goog-api-key", Key } },
        Content = Payload(body),
    };

}
