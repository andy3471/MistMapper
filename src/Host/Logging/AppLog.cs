namespace MistMapper.Host.Logging;

public static class AppLog
{
    public static IAppLog Current { get; set; } = new FileAppLog();
}
