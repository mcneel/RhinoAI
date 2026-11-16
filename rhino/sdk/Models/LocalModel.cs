using System;

namespace Rhino.AI.Models;

// https://lmstudio.ai/docs/developer/openai-compat

/// <summary>
/// A model served by an OpenAI-compatible server on the local machine or network
/// (LM Studio, llama.cpp, Ollama, vLLM, and so on). The vendor name comes from the definition,
/// so usage and permissions are attributed to the server the user actually runs.
/// </summary>
internal sealed class LocalModel : ApiModel
{

    private LocalModel(string name, string vendor, Uri server, Protocol protocol) : base(name, vendor, server, protocol)
    {

    }

    /// <summary>
    /// The default Local Model, using the chat completions protocol.
    /// </summary>
    public static LocalModel Default(string modelName, string vendor, Uri server) => Completions(modelName, vendor, server);
    public static LocalModel Completions(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.ChatCompletions("/v1/chat/completions"));
    public static LocalModel Anthropic(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.Messages("/v1/messages"));
    public static LocalModel Responses(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.Responses("/v1/responses"));

}
