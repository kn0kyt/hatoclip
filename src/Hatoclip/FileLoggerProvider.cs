using Microsoft.Extensions.Logging;

namespace Hatoclip;

/// <summary>Routes ASP.NET Core / Kestrel log messages (Warning and above) into <see cref="Log"/>.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var level = logLevel switch
            {
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                _ => "CRIT",
            };
            var message = $"{category}: {formatter(state, exception)}";
            Hatoclip.Log.Write(level, exception is null ? message : $"{message} {exception}");
        }
    }
}
