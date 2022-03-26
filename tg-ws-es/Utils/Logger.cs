using System.Diagnostics;
using System.Reflection;

public class Logger
{
    public enum LogLevel
    {
        DEBUG = 0,
        INFO = 1,
        WARN = 2,
        ERROR = 3,
        FATAL = 4
    }
    public enum LogType
    {
        OnlyConsole = 0,
        OnlyLogFile = 1,
        ConsoleWithLogFile = 2
    }
    public static void Trace(object message, LogLevel? logLevel = LogLevel.INFO, LogType? logType = LogType.OnlyConsole, string? logFilePath = "")
    {
        string? methodName = new StackTrace().GetFrame(1)?.GetMethod()?.Name;
        string? assemblyName = Assembly.GetCallingAssembly().GetName().Name;
        if (logType != LogType.OnlyLogFile)
        {
            switch (logLevel)
            {
                case LogLevel.WARN:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.ERROR:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogLevel.DEBUG:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    break;
            }
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss:fff} {logLevel}] [{assemblyName} - {methodName}] {message}");
            Console.ResetColor();
        }
        if (logType != LogType.OnlyConsole)
        {
            string path = string.IsNullOrWhiteSpace(logFilePath) ? $"logs\\{DateTime.Now:yyyy-MM-dd}.log" : logFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
            File.AppendAllLines(path, new[] { $"[{DateTime.Now} {logLevel}] [{assemblyName} - {methodName}] {message}" });
        }
    }
}
