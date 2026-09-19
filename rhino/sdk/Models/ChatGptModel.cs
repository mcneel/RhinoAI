using System;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Rhino.AI.Models;

public sealed class ChatGptModel : ApiModel
{

    private const string Host = "https://api.openai.com/v1/responses";

    public ChatGptModel(string name) : base(name, "OpenAI", new ChatGptSerializationConverter())
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    protected override HttpRequestMessage GetRequest(JsonObject body) => new(HttpMethod.Post, Host)
    {
        Headers = { { "Authorization", $"Bearer {Key}" } },
        Content = Payload(body),
    };

}
