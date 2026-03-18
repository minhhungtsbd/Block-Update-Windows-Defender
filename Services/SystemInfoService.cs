using System.Management;
using System.Threading.Tasks;
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

                using (var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject os in results)
                    {
                        model.Caption = os["Caption"]?.ToString() ?? "Unknown";
                        model.Version = os["Version"]?.ToString() ?? "Unknown";
                        model.BuildNumber = os["BuildNumber"]?.ToString() ?? "Unknown";
                        model.Architecture = os["OSArchitecture"]?.ToString() ?? "Unknown";
                        break;
                    }
                }

                return model;
            });
        }
    }
}
