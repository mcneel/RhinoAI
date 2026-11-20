using System;

namespace Rhino.AI.Models;

/// <summary>
/// An Unknown LLM Model
/// </summary>
internal sealed class UnknownModel : ApiModel
{

    private UnknownModel(string name, string vendor, Uri server, Protocol protocol) : base(name, vendor, server, protocol)
    {

    }

    public static UnknownModel Completions(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.ChatCompletions("/v1/chat/completions"));
    public static UnknownModel Anthropic(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.Messages("/v1/messages"));
    public static UnknownModel Responses(string modelName, string vendor, Uri server) => new(modelName, vendor, server, Protocol.Responses("/v1/responses"));

}

public enum ProtocolKind { Default, Messages, Completions, Responses }
