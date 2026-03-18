using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class WindowsUpdateService
    {
        private const string WindowsUpdatePolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string AutomaticUpdatePolicyPath = WindowsUpdatePolicyPath + @"\AU";

        public Task<WindowsUpdateStatus> GetStatusAsync()
        {
            return Task.Run(() =>
            {
                var disabledByPolicy = GetRegistryDword(AutomaticUpdatePolicyPath, "NoAutoUpdate") == 1;
                var updateAccessBlocked = GetRegistryDword(WindowsUpdatePolicyPath, "DisableWindowsUpdateAccess") == 1;
                var wuauservInfo = GetServiceInfo("wuauserv");
                var usoSvcInfo = GetServiceInfo("UsoSvc");
                var waaSMedicSvcInfo = GetServiceInfo("WaaSMedicSvc");

                var hardenedOff = disabledByPolicy &&
                                  updateAccessBlocked &&
                                  IsEffectivelyDisabled(wuauservInfo) &&
                                  IsEffectivelyDisabled(usoSvcInfo);

                var manualUpdateCapability = GetManualUpdateCapability(
                    disabledByPolicy,
                    updateAccessBlocked,
                    wuauservInfo,
                    usoSvcInfo,
                    waaSMedicSvcInfo);

                return new WindowsUpdateStatus
                {
                    IsEnabled = !disabledByPolicy,
                    StatusText = hardenedOff ? "Disabled (Hardened)" : (disabledByPolicy ? "Disabled" : "Enabled"),
                    DetailText = hardenedOff
                        ? "Automatic Updates is disabled by policy and core update services are hardened where Windows allows it. Review Verification for exact service states."
                        : (disabledByPolicy
                            ? "Automatic Updates is disabled by local policy. Review Verification to confirm how much manual update access remains."
                            : "Automatic Updates is enabled. Windows Update services are allowed to run normally."),
                    ServiceState = wuauservInfo.DisplayText,
                    PolicyStateText = GetPolicyStateText(disabledByPolicy, updateAccessBlocked),
                    WuauservStateText = wuauservInfo.DisplayText,
                    UsoSvcStateText = usoSvcInfo.DisplayText,
                    WaaSMedicSvcStateText = waaSMedicSvcInfo.DisplayText,
                    ManualUpdateCapabilityText = manualUpdateCapability
                };
            });
        }

        public async Task<WindowsUpdateStatus> EnableAsync()
        {
            await Task.Run(() =>
            {
                SetRegistryDword(AutomaticUpdatePolicyPath, "NoAutoUpdate", 0);
                SetRegistryDword(WindowsUpdatePolicyPath, "DisableWindowsUpdateAccess", 0);
                SetServiceStartMode("wuauserv", 3);
                SetServiceStartMode("UsoSvc", 3);
                SetServiceStartMode("WaaSMedicSvc", 3);
                TryStartService("wuauserv");
            });

            return await GetStatusAsync();
        }

        public async Task<WindowsUpdateStatus> DisableAsync()
        {
            await Task.Run(() =>
            {
                SetRegistryDword(AutomaticUpdatePolicyPath, "NoAutoUpdate", 1);
                SetRegistryDword(WindowsUpdatePolicyPath, "DisableWindowsUpdateAccess", 1);
                TryStopService("UsoSvc");
                TryStopService("wuauserv");
                TryStopService("WaaSMedicSvc");
                SetServiceStartMode("UsoSvc", 4);
                SetServiceStartMode("wuauserv", 4);
                SetServiceStartMode("WaaSMedicSvc", 4);
                TryStopService("UsoSvc");
                TryStopService("wuauserv");
                TryStopService("WaaSMedicSvc");
            });

            return await GetStatusAsync();
        }

        private static string GetPolicyStateText(bool noAutoUpdate, bool updateAccessBlocked)
        {
            if (noAutoUpdate && updateAccessBlocked)
            {
                return "Automatic Updates disabled and Windows Update access blocked";
            }

            if (noAutoUpdate)
            {
                return "Automatic Updates disabled";
            }

            if (updateAccessBlocked)
            {
                return "Windows Update access blocked";
            }

            return "No blocking policy";
        }

        private static string GetManualUpdateCapability(
            bool noAutoUpdate,
            bool updateAccessBlocked,
            ServiceInfo wuauservInfo,
            ServiceInfo usoSvcInfo,
            ServiceInfo waaSMedicSvcInfo)
        {
            var wuaBlocked = IsEffectivelyDisabled(wuauservInfo);
            var usoBlocked = IsEffectivelyDisabled(usoSvcInfo);
            var medicBlocked = IsEffectivelyDisabled(waaSMedicSvcInfo);

            if (noAutoUpdate && updateAccessBlocked && wuaBlocked && usoBlocked)
            {
                return medicBlocked
                    ? "Likely blocked"
                    : "Partially blocked (WaaSMedicSvc may recover update components)";
            }

            if (noAutoUpdate || updateAccessBlocked || wuaBlocked || usoBlocked)
            {
                return "Still possible";
            }

            return "Available";
        }

        private static bool IsEffectivelyDisabled(ServiceInfo info)
        {
            return !info.Exists || string.Equals(info.StartMode, "Disabled");
        }

        private static ServiceInfo GetServiceInfo(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    return new ServiceInfo
                    {
                        Exists = true,
                        State = service.Status.ToString(),
                        StartMode = GetServiceStartMode(serviceName)
                    };
                }
            }
            catch
            {
                return new ServiceInfo
                {
                    Exists = false,
                    State = "Unavailable",
                    StartMode = "Unavailable"
                };
            }
        }

        private static string GetServiceStartMode(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"))
                {
                    var value = key?.GetValue("Start");
                    if (value is int)
                    {
                        switch ((int)value)
                        {
                            case 2:
                                return "Automatic";
                            case 3:
                                return "Manual";
                            case 4:
                                return "Disabled";
                            default:
                                return value.ToString();
                        }
                    }
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private static int GetRegistryDword(string keyPath, string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    var value = key?.GetValue(valueName);
                    return value is int ? (int)value : 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static void SetRegistryDword(string keyPath, string valueName, int value)
        {
            using (var key = Registry.LocalMachine.CreateSubKey(keyPath))
            {
                key?.SetValue(valueName, value, RegistryValueKind.DWord);
            }
        }

        private static void SetServiceStartMode(string serviceName, int startValue)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true))
                {
                    key?.SetValue("Start", startValue, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }

        private static void TryStopService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status == ServiceControllerStatus.Stopped ||
                        service.Status == ServiceControllerStatus.StopPending)
                    {
                        return;
                    }

                    if (service.CanStop)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, System.TimeSpan.FromSeconds(15));
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryStartService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status == ServiceControllerStatus.Running ||
                        service.Status == ServiceControllerStatus.StartPending)
                    {
                        return;
                    }

                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, System.TimeSpan.FromSeconds(15));
                }
            }
            catch
            {
            }
        }

        private class ServiceInfo
        {
            public bool Exists { get; set; }
            public string State { get; set; }
            public string StartMode { get; set; }
            public string DisplayText => Exists ? $"{State} / {StartMode}" : "Unavailable";
        }
    }
}
