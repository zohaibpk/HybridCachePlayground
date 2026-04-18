namespace HybridCachePlayground.Web.Services;

/// <summary>
/// Singleton that holds the path to the current startup log file.
/// Registered in Program.cs after the startup timestamp is known.
/// </summary>
public sealed class LogFilePathProvider
{
    public string CurrentLogPath { get; }

    public LogFilePathProvider(string logPath)
    {
        CurrentLogPath = logPath;
    }
}
