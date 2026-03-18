using System.Threading.Tasks;
using Microsoft.Win32;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class SystemInfoService
    {
        public Task<SystemInfoModel> GetSystemInfoAsync()
        {
            return Task.Run(() =>
            {
                var model = new SystemInfoModel
                {
                    ComputerName = System.Environment.MachineName
                };

                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        model.Caption = key?.GetValue("ProductName")?.ToString() ?? "Unknown";
                        model.Version = key?.GetValue("CurrentVersion")?.ToString() ?? System.Environment.OSVersion.Version.ToString();
                        model.BuildNumber = key?.GetValue("CurrentBuildNumber")?.ToString()
                                            ?? key?.GetValue("CurrentBuild")?.ToString()
                                            ?? "Unknown";
                        model.Architecture = System.Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                    }
                }
                catch
                {
                    model.Caption = "Unknown";
                    model.Version = System.Environment.OSVersion.Version.ToString();
                    model.BuildNumber = System.Environment.OSVersion.Version.Build.ToString();
                    model.Architecture = System.Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                }

                return model;
            });
        }
    }
}
