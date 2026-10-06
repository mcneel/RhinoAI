using System;
using System.Net.Http.Headers;

namespace Rhino.AI.Models;

/// <summary>One wire format: how turns are serialized, where they are posted relative to a host, and how the key is sent.</summary>
internal sealed record Protocol(Func<string, ITurnConverter> Converter, Func<string, string> Route, Action<HttpRequestHeaders, string> Authorize)
{

    public static Protocol ChatCompletions(string path) => new(vendor => new ChatCompletionsSerializationConverter(vendor), _ => path, Bearer);

    public static Protocol Messages(string path) => new(vendor => new ClaudeSerializationConverter(vendor), _ => path, AnthropicKey);

    public static Protocol Responses(string path) => new(vendor => new ChatGptSerializationConverter(vendor), _ => path, Bearer);

    public static Protocol GenerateContent(string path) => new(vendor => new GeminiSerializationConverter(vendor), model => $"{path}/{model}:generateContent", GoogleKey);

    private static void Bearer(HttpRequestHeaders headers, string key) => headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

    private static void AnthropicKey(HttpRequestHeaders headers, string key)
    {
        headers.Add("x-api-key", key);
        headers.Add("anthropic-version", "2023-06-01");
    }

    private static void GoogleKey(HttpRequestHeaders headers, string key) => headers.Add("x-goog-api-key", key);

}
