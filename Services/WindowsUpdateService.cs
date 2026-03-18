using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class WindowsUpdateService
    {
        private const string UpdatePolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

        public Task<WindowsUpdateStatus> GetStatusAsync()
        {
            return Task.Run(() =>
            {
                var disabledByPolicy = false;
                using (var key = Registry.LocalMachine.OpenSubKey(UpdatePolicyPath))
                {
                    var value = key?.GetValue("NoAutoUpdate");
                    disabledByPolicy = value is int intValue && intValue == 1;
                }

                var serviceState = GetServiceState("wuauserv");
                return new WindowsUpdateStatus
                {
                    IsEnabled = !disabledByPolicy,
                    StatusText = disabledByPolicy ? "Disabled" : "Enabled",
                    DetailText = disabledByPolicy
                        ? $"Automatic Updates is disabled by local policy. Windows Update service: {serviceState}."
                        : $"Automatic Updates is enabled. Windows Update service: {serviceState}.",
                    ServiceState = serviceState
                };
            });
        }

        public async Task<WindowsUpdateStatus> EnableAsync()
        {
            await Task.Run(() =>
            {
                using (var key = Registry.LocalMachine.CreateSubKey(UpdatePolicyPath))
                {
                    key?.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
                }
            });

            return await GetStatusAsync();
        }

        public async Task<WindowsUpdateStatus> DisableAsync()
        {
            await Task.Run(() =>
            {
                using (var key = Registry.LocalMachine.CreateSubKey(UpdatePolicyPath))
                {
                    key?.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                }
            });

            return await GetStatusAsync();
        }

        private static string GetServiceState(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    return service.Status.ToString();
                }
            }
            catch
            {
                return "Unavailable";
            }
        }
    }
}
