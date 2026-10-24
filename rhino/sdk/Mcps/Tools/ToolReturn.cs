using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>The explicit result of a tool call</summary>
public enum ToolResult { Success, Mixed, Failure }

/// <summary>
/// A unified Tool call return
/// </summary>
public sealed record ToolReturn
{

    /// <summary>The content of the return</summary>
    public IReadOnlyList<IMessageContent> Content { get; }

    /// <summary>The final result of the tool</summary>
    public ToolResult Result { get; }

    /// <summary>Guidance for the AI if the return fails</summary>
    public string? Guidance { get; }

    public string Message => string.Concat(Content.OfType<TextContent>().Select(p => p.Text));

    public ToolReturn(IEnumerable<IMessageContent> content, ToolResult result, string? guidance)
    {
        Content = [.. content];
        Result = result;
        Guidance = guidance;
    }

    public ToolReturn(string message, ToolResult result, string? guidance)
        : this([new TextContent(message)], result, guidance)
    {
    }

    /// <summary>A copy of the return</summary>
    /// <returns>A deep copy</returns>
    public ToolReturn Copy() => new(Content.Select(c => c.Copy()), Result, Guidance);

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
    /// A successful tool call
    /// </summary>
    /// <param name="content">A summary of the result</param>
    public static ToolReturn Success(IMessageContent content) => new([content], ToolResult.Success, null);

    /// <summary>
    /// A successful tool call
    /// </summary>
    /// <param name="contents">A summary of the results</param>
    public static ToolReturn Success(IEnumerable<IMessageContent> contents) => new(contents, ToolResult.Success, null);

    /// <summary>
    /// A refused tool call
    /// </summary>
    internal static ToolReturn Refused() => new("Tool use was refused", ToolResult.Failure, "Ask the user what to do");

}
