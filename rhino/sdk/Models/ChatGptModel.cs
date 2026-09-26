using System;

namespace Rhino.AI.Models;

/// <summary>A ChatGPT API Model</summary>
internal sealed class ChatGptModel : ApiModel
{

    private static Uri Host { get; } = new("https://api.openai.com");

    private ChatGptModel(string name, Protocol protocol) : base(name, "OpenAI", Host, protocol)
    {
        ApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        ApiKey ??= Secrets.Stasher.GetSecret("OPENAI_API_KEY");
    }
    
    /// <summary>A ChatGPT Model using the default protocol.</summary>
    public static ChatGptModel Default(string modelName) => Responses(modelName);
    
    /// <summary>A ChatGPT Model using the responses protocol.</summary>
    public static ChatGptModel Responses(string modelName) => new(modelName, Protocol.Responses("/v1/responses"));
    
    /// <summary>A ChatGPT Model using the completions protocol.</summary>
    public static ChatGptModel Completions(string modelName) => new(modelName, Protocol.ChatCompletions("/v1/chat/completions"));

}
