using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LifeOs.Tests;

/// <summary>
/// Keeps what the host logs, for a test that checks a log line: pass <see cref="AddTo"/> as
/// <c>ApiTestHost.StartAsync</c>'s <c>services</c>.
/// </summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, string Message)> lines = new();

    public void AddTo(IServiceCollection services) => services.AddSingleton<ILoggerProvider>(this);

    /// <summary>The messages logged under <paramref name="category"/>, oldest first.</summary>
    public IReadOnlyList<string> Messages(string category)
        => lines.Where(l => l.Category == category).Select(l => l.Message).ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                capture.lines.Enqueue((category, formatter(state, exception)));
            }
        }
    }
}
