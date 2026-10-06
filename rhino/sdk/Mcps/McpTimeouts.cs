using System;

namespace Rhino.AI;

internal static class McpTimeouts
{

    public static TimeSpan ToolCall { get; } = TimeSpan.FromMinutes(10);

    public static TimeSpan Startup { get; } = TimeSpan.FromMinutes(2);

}
