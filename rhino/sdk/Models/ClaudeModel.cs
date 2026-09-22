using System;

namespace Rhino.AI.Models;

/// <summary>A Claude API Model</summary>
internal sealed class ClaudeModel : ApiModel
{

    private static Uri Host { get; } = new("https://api.anthropic.com");

    private ClaudeModel(string name, Protocol protocol) : base(name, "Anthropic", Host, protocol)
    {
        ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    /// <summary>A Claude Model using the default protocol.</summary>
    public static ClaudeModel Default(string modelName) => Messages(modelName);
    
    /// <summary>A Claude Model using the Anthropic protocol.</summary>
    public static ClaudeModel Messages(string modelName) => new(modelName, Protocol.Messages("/v1/messages"));
    
    /// <summary>A Claude Model using the completions protocol.</summary>
    public static ClaudeModel Completions(string modelName) => new(modelName, Protocol.ChatCompletions("/v1/chat/completions"));

}
