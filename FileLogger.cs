using System.Text;

namespace RockyBackup;

public sealed class FileLogger
{
    private readonly string _logDirectory;
    private readonly object _sync = new();

    public FileLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        var file = Path.Combine(_logDirectory, $"backup-{DateTime.Now:yyyyMMdd}.log");
        lock (_sync)
        {
            File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
