using System;

namespace Rhino.AI.Models;

// https://api-docs.deepseek.com/

/// <summary>
/// The DeepSeek AI Model
/// </summary>
internal sealed class DeepSeekModel : ApiModel
{

    private static Uri Host { get; } = new("https://api.deepseek.com");

    private DeepSeekModel(string name, Protocol protocol) : base(name, "DeepSeek", Host, protocol)
    {
        ApiKey ??= Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        ApiKey ??= Secrets.Stasher.GetSecret("DEEPSEEK_API_KEY");
    }

    /// <summary>A DeepSeek Model using the default protocol.</summary>
    public static DeepSeekModel Default(string modelName = "deepseek-flash") => Completions(modelName);
    
    /// <summary>A DeepSeek Model using the completions protocol.</summary>
    public static DeepSeekModel Completions(string modelName) => new(modelName, Protocol.ChatCompletions("/chat/completions"));
    
    /// <summary>A DeepSeek Model using the Anthropic protocol.</summary>
    public static DeepSeekModel Anthropic(string modelName) => new(modelName, Protocol.Messages("/anthropic/v1/messages"));
    
    /// <summary>A DeepSeek Model using the responses protocol.</summary>
    public static DeepSeekModel Responses(string modelName) => new(modelName, Protocol.Responses("/responses"));

}
