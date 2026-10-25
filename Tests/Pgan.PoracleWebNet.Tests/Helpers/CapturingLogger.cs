using Microsoft.Extensions.Logging;

namespace Pgan.PoracleWebNet.Tests.Helpers;

/// <summary>Keeps every formatted message, so a test can assert what a log line would print.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => this._messages;

    public string All => string.Join('\n', this._messages);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        this._messages.Add(formatter(state, exception));
    }
}
