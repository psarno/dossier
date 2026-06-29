namespace DossierApi.Services;

public record LogEntry(string Timestamp, string Level, string Message);

public class PipelineLog
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public void Info(string message) => Add("INFO", message);
    public void Warn(string message) => Add("WARN", message);
    public void Error(string message) => Add("ERROR", message);

    private void Add(string level, string message)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(DateTime.UtcNow.ToString("HH:mm:ss.fff"), level, message));
            if (_entries.Count > 1000)
                _entries.RemoveAt(0);
        }
    }

    public List<LogEntry> GetAll()
    {
        lock (_lock) return [.._entries];
    }
}
