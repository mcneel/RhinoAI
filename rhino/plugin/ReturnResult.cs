
namespace RhinoAI;

// TODO : May want a dictionary or something for Key Value Pairs

/// <summary>
/// Universal Return Result for MCP Tool Calls
/// </summary>
/// <param name="Result"></param>
/// <param name="Message"></param>
/// <param name="Guidance"></param>
internal record struct ReturnResult(bool Result, string? Message, string? Guidance)
{

    public static ReturnResult Success(string? message = null) => new(true, null, null);

    public static ReturnResult Failure(string message, string? guidance = null) => new(false, message, guidance);

    public static implicit operator bool(ReturnResult result) => result.Result;
    public static implicit operator string(ReturnResult result) => result.AsJson();

    public readonly string AsJson()
    {
        var resultObject = Result switch
        {
            true => new { error = NULL_STRING, message = Message, guidance = NULL_STRING },
            false => new { error = Message, message = NULL_STRING, guidance = Guidance },
        };

        return JsonSerializer.Serialize(resultObject, McpSerializer.Options);
    }

    private const string? NULL_STRING = null;
    
}
