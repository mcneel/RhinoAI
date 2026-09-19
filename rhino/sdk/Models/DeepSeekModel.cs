using System;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Rhino.AI.Models;

// https://api-docs.deepseek.com/api/create-chat-completion

public sealed class DeepSeekModel : ApiModel
{

    private const string Host = "https://api.deepseek.com/chat/completions";

    public DeepSeekModel(string name) : base(name, "DeepSeek", new DeepSeekSerializationConverter())
    {
        ApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
    }

    protected override HttpRequestMessage GetRequest(JsonObject body) => new(HttpMethod.Post, Host)
    {
        Headers = { { "Authorization", $"Bearer {Key}" } },
        Content = Payload(body),
    };

}
