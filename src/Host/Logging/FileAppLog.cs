using System.Globalization;

namespace MistMapper.Host.Logging;

public sealed class FileAppLog : IAppLog
{
    readonly object _gate = new();
    readonly string _logDir;

    public FileAppLog(string? logDirectory = null)
    {
        _logDir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MistMapper",
            "logs");
    }

    public void Info(string message, Exception? ex = null) => Write("INFO", message, ex);
    public void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = FormatLine(level, message, ex);
            lock (_gate)
            {
                Directory.CreateDirectory(_logDir);
                var path = Path.Combine(_logDir, "mistmapper-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow IO errors — logging must not crash the host.
        }
    }

    static string FormatLine(string level, string message, Exception? ex)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{ts} [{level}] {message}";
        if (ex is not null)
            line += Environment.NewLine + ex;
        return line;
    }
}
