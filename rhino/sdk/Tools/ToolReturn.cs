namespace Rhino.AI;

/// <summary>The explicit result of a tool call</summary>
public enum ToolResult { Success, Mixed, Failure }

/// <summary>
/// A unified Tool call return
/// </summary>
    /// <param name="message">A message summarising the result</param>
/// <param name="Result">The result of the Tools call</param>
    /// <param name="guidance">Action the AI Agent should do to resolve this failure</param>
public record struct ToolReturn(string Message, ToolResult Result, string? Guidance)
{
    
    /// <summary>
    /// A Failed Tool Call
    /// </summary>
    /// <param name="message">A message summarising the result</param>
    /// <param name="guidance">Action the AI Agent should do to resolve this failure</param>
    public static ToolReturn Failure(string message, string guidance) => new(message, ToolResult.Failure, guidance);
    
    /// <summary>
    /// A successful tool call
    /// </summary>
    /// <param name="message">A message summarising the result</param>
    public static ToolReturn Success(string message) => new(message, ToolResult.Success, null);
    
    /// <summary>
    /// A refused tool call
    /// </summary>
    internal static ToolReturn Refused() => new ("Tool use was refused", ToolResult.Failure, "Ask the user what to do");

}
