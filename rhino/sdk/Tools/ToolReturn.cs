using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rhino.AI;

public enum ToolResult { Success, Mixed, Failure }

public record struct ToolReturn(string Message, ToolResult Result, string? Guidance)
{
    public static ToolReturn Failure(string message, string guidance) => new(message, ToolResult.Failure, guidance);
    public static ToolReturn Success(string message) => new(message, ToolResult.Success, null);
    internal static ToolReturn Refused() => new ("Tool use was refused", ToolResult.Failure, "Ask the user what to do");

}
