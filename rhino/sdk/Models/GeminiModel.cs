using System;

namespace Rhino.AI.Models;

// https://ai.google.dev/gemini-api/docs#rest

/// <summary>
/// The Gemini Model.
/// </summary>
internal sealed class GeminiModel : ApiModel
{

    private static Uri Host { get; } = new("https://generativelanguage.googleapis.com");

    private GeminiModel(string name, Protocol protocol) : base(name, "Google", Host, protocol)
    {
        ApiKey ??= Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        ApiKey ??= Secrets.Stasher.GetSecret("GEMINI_API_KEY");
    }

    /// <summary>A Gemini Model using the default protocol.</summary>
    public static GeminiModel Default(string modelName) => GenerateContent(modelName);
    
    /// <summary>A Gemini Model using the content protocol.</summary>
    public static GeminiModel GenerateContent(string modelName) => new(modelName, Protocol.GenerateContent("/v1beta/models"));
    
    /// <summary>A Gemini Model using the completions protocol.</summary>
    public static GeminiModel Completions(string modelName) => new(modelName, Protocol.ChatCompletions("/v1beta/openai/chat/completions"));

}
