
namespace RhinoAI;

// TODO : May want a dictionary or something for Key Value Pairs

/// <summary>
/// Universal Return Result for MCP Tool Calls
/// </summary>
/// <param name="Result">True for success, false for Failure</param>
/// <param name="Message">Any relevant message about the success or failure</param>
/// <param name="Guidance">How can an AI Agent respond to this?</param>
internal record struct ReturnResult(bool Result, string? Message, string? Guidance)
{

    public static ReturnResult Success(string? message = null) => new(true, message, null);

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
