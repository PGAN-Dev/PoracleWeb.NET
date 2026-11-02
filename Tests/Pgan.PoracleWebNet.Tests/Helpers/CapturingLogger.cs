using Microsoft.Extensions.Logging;

namespace Pgan.PoracleWebNet.Tests.Helpers;

/// <summary>Keeps every formatted message, so a test can assert what a log line would print.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly List<string> _messages = [];

    /// <summary>Every message with the level it was logged at.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => this._entries;

    public IReadOnlyList<string> Messages => this._messages;

    public string All => string.Join('\n', this._messages);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var message = formatter(state, exception);
        this._entries.Add((logLevel, message));
        this._messages.Add(message);
    }
}
