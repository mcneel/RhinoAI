using System;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Rhino.AI.Models;

public sealed class ClaudeModel : ApiModel
{

    private const string Host = "https://api.anthropic.com/v1/messages";

    public ClaudeModel(string name) : base(name, "Anthropic", new ClaudeSerializationConverter())
    {
        ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    protected override HttpRequestMessage GetRequest(JsonObject body) => new(HttpMethod.Post, Host)
    {
        Headers =
        {
            { "x-api-key", Key },
            { "anthropic-version", "2023-06-01" },
        },
        Content = Payload(body),
    };

}
