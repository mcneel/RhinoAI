using Microsoft.Extensions.Logging;

namespace RhMcp;

internal sealed class RhinoLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RhinoLogger(categoryName);
    public void Dispose() { }

    private sealed class RhinoLogger : ILogger
    {
        private readonly string _category;
        public RhinoLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            RhinoApp.WriteLine($"[Rhino MCP][{logLevel}] {_category}: {msg}");
            if (exception is not null)
                RhinoApp.WriteLine($"[Rhino MCP]   {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
        }
    }
}
