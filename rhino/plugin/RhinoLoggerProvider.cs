using Microsoft.Extensions.Logging;

namespace RhMcp;

internal sealed class RhinoLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RhinoLogger(categoryName);
    public void Dispose() { }

    private sealed class RhinoLogger(string category) : ILogger
    {
        private string Category { get; } = category;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

#if DEBUG
        private const LogLevel MinLevel = LogLevel.Information;
#else
        private const LogLevel MinLevel = LogLevel.Warning;
#endif

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            RhinoApp.WriteLine($"[Rhino MCP][{logLevel}] {Category}: {msg}");
            if (exception is not null)
                RhinoApp.WriteLine($"[Rhino MCP]   {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    }
}
