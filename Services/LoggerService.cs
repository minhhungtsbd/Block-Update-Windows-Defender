using System;
using System.IO;

namespace BlockUpdateWindowsDefender.Services
{
    public class LoggerService
    {
        private readonly string _logFilePath;

        public LoggerService()
        {
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlockUpdateWindowsDefender");
            Directory.CreateDirectory(appFolder);
            _logFilePath = Path.Combine(appFolder, "activity.log");
        }

        public string LogFilePath => _logFilePath;

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllLines(_logFilePath, new[] { line });
        }
    }
}
