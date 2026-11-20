using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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

    [JsonConstructor]
    private ToolReturn(IReadOnlyList<IMessageContent> content, ToolResult result, string? guidance)
        : this((IEnumerable<IMessageContent>)content, result, guidance)
    {
    }

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
    internal static ToolReturn Refused(string toolName) => new($"Use of {toolName} was refused", ToolResult.Failure, "Ask the user what to do");

    internal static ToolReturn MissingArgument(string toolName, string argName, string expected)
        => Failure($"Tool '{toolName}' requires argument '{argName}'.", $"Call '{toolName}' again with '{argName}' set to {expected}.");

    internal static ToolReturn MissingArgument(string toolName, string argName)
        => Failure($"Tool '{toolName}' requires argument '{argName}'.", $"Call '{toolName}' again with '{argName}' supplied.");

    internal static ToolReturn UnknownArgument(string toolName, string argName)
        => Failure($"Tool '{toolName}' has no argument '{argName}'.", $"Call '{toolName}' again with only the arguments it declares.");

    internal static ToolReturn UnknownTool(string mcpName, string toolName)
        => Failure($"MCP '{mcpName}' has no tool '{toolName}'.", "Call one of the tools offered in this session.");

    internal static ToolReturn UnknownMcp(string mcpName, IEnumerable<string> available)
        => Failure($"No MCP named '{mcpName}'.", $"Use one of: {string.Join(", ", available)}.");

    internal static ToolReturn HttpFailure(HttpStatusCode status)
    {
        string guidance = (int)status switch
        {
            404 or 410 => "The page does not exist. Check the url for typos, or find the right page another way.",
            401 or 403 => "The page needs a login or blocks automated clients. Do not retry; use another source or ask the user.",
            429 => "The site is rate limiting requests. Do not retry straight away.",
            >= 500 and <= 599 => "The site had a server fault. Retry once at most, then use another source.",
            _ => "Do not retry the same call unchanged.",
        };

        return Failure($"The url responded with status code {(int)status} ({status}).", guidance);
    }

    internal static ToolReturn FileIsDirectory()
        => Failure("File is a directory.", "Pass the path of a file, not a folder.");

    internal static ToolReturn FileNotFound()
        => Failure("File does not exist.", "Check the path, or create the file with the write tool first.");

    internal static ToolReturn FileAccessDenied(string verb)
        => Failure($"Did not have permission to {verb} file", $"Ask the user to grant access, or {verb} a different file.");

    internal static ToolReturn FileError(Exception exception, string pastVerb)
        => Failure(exception.Message, $"The file could not be {pastVerb}. Do not retry the same call unchanged.");
}
