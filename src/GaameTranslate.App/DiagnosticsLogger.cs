using Microsoft.Extensions.Logging;

namespace GaameTranslate.App;

/// <summary>No user text, URLs, exception payloads or credentials are logged.</summary>
public sealed class DiagnosticsLoggerProvider(string directory) : ILoggerProvider
{
    private readonly string logDirectory = directory;
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);
    public void Dispose() { }
    private sealed class Writer(DiagnosticsLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try
            {
                lock (owner.gate)
                {
                    Directory.CreateDirectory(owner.logDirectory); var path = Path.Combine(owner.logDirectory, "diagnostics.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".1", true);
                    File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{level}] {category} {formatter(state, null)}{Environment.NewLine}");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
