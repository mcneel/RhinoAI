using System;

namespace Rhino.AI.Models;

// https://lmstudio.ai/docs/developer/openai-compat

/// <summary>
/// A Local LLM Model
/// </summary>
internal sealed class LocalModel : ApiModel
{

    private LocalModel(string name, Uri server, Protocol protocol) : base(name, "LM Studio", server, protocol)
    {
        
    }

    /// <summary>
    /// The Default LM Studio Model.
    /// </summary>
    public static LocalModel Default(string modelName, Uri server) => Completions(modelName, server);
    public static LocalModel Completions(string modelName, Uri server) => new(modelName, server, Protocol.ChatCompletions("/v1/chat/completions"));
    public static LocalModel Anthropic(string modelName, Uri server) => new(modelName, server, Protocol.Messages("/v1/messages"));
    public static LocalModel Responses(string modelName, Uri server) => new(modelName, server, Protocol.Responses("/v1/responses"));

}
