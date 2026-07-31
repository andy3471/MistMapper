namespace MistMapper.Host.Logging;

public interface IAppLog
{
    void Info(string message, Exception? ex = null);
    void Warn(string message, Exception? ex = null);
    void Error(string message, Exception? ex = null);
}
