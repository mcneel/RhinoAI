using System;

namespace Rhino.AI.Models;

// https://ai.google.dev/gemini-api/docs#rest

/// <summary>
/// The Gemini Model.
/// </summary>
internal sealed class GeminiModel : ApiModel
{

    private static Uri Host { get; } = new("https://generativelanguage.googleapis.com");

    private GeminiModel(string name, string vendor, Protocol protocol) : base(name, vendor, Host, protocol)
    {
    }

    /// <summary>A Gemini Model using the default protocol.</summary>
    public static GeminiModel Default(string modelName, string vendor) => GenerateContent(modelName, vendor);
    
    public static GeminiModel GenerateContent(string modelName, string vendor) => new(modelName, vendor, Protocol.GenerateContent("/v1beta/models"));
    
    /// <summary>A Gemini Model using the completions protocol.</summary>
    public static GeminiModel Completions(string modelName, string vendor) => new(modelName, vendor, Protocol.ChatCompletions("/v1beta/openai/chat/completions"));

}
