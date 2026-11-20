using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SecureLab.Api.Tests;

// перехоплює логи в пам'ять під час тесту, щоб можна було перевірити їхній вміст без читання файлів
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
