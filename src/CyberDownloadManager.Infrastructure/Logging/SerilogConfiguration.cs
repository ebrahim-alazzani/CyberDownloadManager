using Serilog;

namespace CyberDownloadManager.Infrastructure.Logging;

public static class SerilogConfiguration
{
    public static ILogger CreateLogger(string logFilePath)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
