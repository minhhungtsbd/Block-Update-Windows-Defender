using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace BlockUpdateWindowsDefender.Services
{
    public class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "BlockUpdateWindowsDefender";

        public bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                var value = key?.GetValue(AppValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        public void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    key?.SetValue(AppValueName, BuildCommandValue(), RegistryValueKind.String);
                }
                else
                {
                    key?.DeleteValue(AppValueName, false);
                }
            }
        }

        private static string BuildCommandValue()
        {
            var location = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
            {
                location = Process.GetCurrentProcess().MainModule?.FileName;
            }

            return $"\"{location}\"";
        }
    }
}
