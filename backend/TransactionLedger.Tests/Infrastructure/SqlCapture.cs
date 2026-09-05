using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// Captures the SQL EF Core actually sends, so H8 can assert on it.
///
/// H8 is the test that makes BR-40 real rather than an unverified claim:
/// "pagination happens in SQL" is only true if LIMIT and OFFSET appear in the
/// command text. An in-memory Skip would produce identical HTTP responses
/// while transferring every row over the wire, so no assertion on the response
/// body could ever catch it — only the generated SQL can.
/// </summary>
public sealed class SqlCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyList<string> Commands => _commands.ToArray();

    public void Clear() => _commands.Clear();

    public ILogger CreateLogger(string categoryName) =>
        categoryName == DbLoggerCategory.Database.Command.Name
            ? new CommandLogger(_commands)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class CommandLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _commands;

        public CommandLogger(ConcurrentQueue<string> commands)
        {
            _commands = commands;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _commands.Enqueue(formatter(state, exception));
            }
        }
    }
}
