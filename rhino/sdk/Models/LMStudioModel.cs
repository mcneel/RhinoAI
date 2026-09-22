using System;

namespace Rhino.AI.Models;

// https://lmstudio.ai/docs/developer/openai-compat

/// <summary>
/// The LM Studio Model
/// </summary>
internal sealed class LMStudioModel : ApiModel
{

    public static Uri DefaultServer { get; } = new("http://localhost:1234");

    private LMStudioModel(string name, Uri server, Protocol protocol) : base(name, "LM Studio", server, protocol)
    {
    }

    /// <summary>
    /// The Default LM Studio Model.
    /// </summary>
    public static LMStudioModel Default(string modelName) => Completions(modelName, DefaultServer);
    public static LMStudioModel Completions(string modelName, Uri server) => new(modelName, server, Protocol.ChatCompletions("/v1/chat/completions"));
    public static LMStudioModel Anthropic(string modelName, Uri server) => new(modelName, server, Protocol.Messages("/v1/messages"));
    public static LMStudioModel Responses(string modelName, Uri server) => new(modelName, server, Protocol.Responses("/v1/responses"));

}
