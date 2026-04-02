using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class SystemMaintenanceService
    {
        private const string RdpTcpPath = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
        private const string RdpWdsPath = @"SYSTEM\CurrentControlSet\Control\Terminal Server\Wds\rdpwd\Tds\tcp";
        private const string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private readonly ProcessRunner _processRunner;

        public SystemMaintenanceService(ProcessRunner processRunner)
        {
            _processRunner = processRunner;
        }

        public async Task<OperationResult> ChangeCurrentUserPasswordAsync(string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = "Password is empty."
                };
            }

            var escapedPassword = newPassword.Replace("'", "''");
            var script =
                "$ErrorActionPreference='Stop';" +
                "$user=$env:USERNAME;" +
                "$password='" + escapedPassword + "';" +
                "$output = net user \"$user\" \"$password\" | Out-String;" +
                "if ($LASTEXITCODE -ne 0) { throw $output };" +
                "Write-Output \"SUCCESS\";";

            var result = await _processRunner.RunPowerShellAsync(script);
            if (!result.IsSuccess)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = error?.Trim()
                };
            }

            try
            {
                ConfigureAutoAdminLogon(Environment.UserName, newPassword);
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = "Windows password was changed, but auto logon settings could not be updated: " + ex.Message
                };
            }

            return new OperationResult
            {
                IsSuccess = true,
                Message = "Windows password changed successfully. Auto logon settings updated."
            };
        }

        public async Task<OperationResult> ChangeRdpPortAsync(int newPort)
        {
            if (newPort < 1024 || newPort > 65535)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = "RDP port must be between 1024 and 65535."
                };
            }

            var currentPort = GetCurrentRdpPort();
            try
            {
                SetRdpPortInRegistry(RdpTcpPath, newPort);
                SetRdpPortInRegistry(RdpWdsPath, newPort);
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = ex.Message
                };
            }

            var firewallTcpResult = await EnsureFirewallRuleAsync(newPort, "TCP");
            if (!firewallTcpResult.IsSuccess)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = firewallTcpResult.Message
                };
            }

            var firewallUdpResult = await EnsureFirewallRuleAsync(newPort, "UDP");
            if (!firewallUdpResult.IsSuccess)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = firewallUdpResult.Message
                };
            }

            var appliedPort = GetCurrentRdpPort();
            if (appliedPort != newPort)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = $"RDP registry verification mismatch. Expected {newPort}, got {appliedPort}."
                };
            }

            var serviceApplyResult = await ApplyRdpPortWithoutRebootAsync();
            if (!serviceApplyResult.IsSuccess)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = "RDP port was changed in registry/firewall, but Remote Desktop Service could not be verified as running. " +
                              "Try reconnecting using new port. If unavailable, reboot VPS once. Details: " + serviceApplyResult.Message
                };
            }

            return new OperationResult
            {
                IsSuccess = true,
                Message = $"RDP port changed from {currentPort} to {newPort}. Remote Desktop Service was restarted. " +
                          "Current session may disconnect briefly, then reconnect with new port."
            };
        }

        public async Task<OperationResult> ExtendSystemDriveAsync()
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "buwd_diskpart_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(scriptPath, "select volume C\r\nextend\r\nexit\r\n");
                var result = await _processRunner.RunAsync("diskpart.exe", $"/s \"{scriptPath}\"");

                var output = (result.StandardOutput ?? string.Empty).Trim();
                var error = (result.StandardError ?? string.Empty).Trim();
                var combined = output + "\n" + error;

                if (combined.IndexOf("DiskPart successfully extended the volume", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new OperationResult
                    {
                        IsSuccess = true,
                        Message = "System drive C: extended successfully."
                    };
                }

                if (combined.IndexOf("There is not enough usable free space", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combined.IndexOf("The volume cannot be extended", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new OperationResult
                    {
                        IsSuccess = false,
                        Message = "No unallocated space available to extend drive C:."
                    };
                }

                if (!result.IsSuccess)
                {
                    return new OperationResult
                    {
                        IsSuccess = false,
                        Message = string.IsNullOrWhiteSpace(error) ? output : error
                    };
                }

                return new OperationResult
                {
                    IsSuccess = true,
                    Message = string.IsNullOrWhiteSpace(output) ? "DiskPart completed." : output
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = ex.Message
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(scriptPath))
                    {
                        File.Delete(scriptPath);
                    }
                }
                catch
                {
                }
            }
        }

        public int GetCurrentRdpPort()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RdpTcpPath))
                {
                    var value = key?.GetValue("PortNumber");
                    if (value is int intValue && intValue > 0)
                    {
                        return intValue;
                    }

                    if (value is long longValue && longValue > 0 && longValue <= 65535)
                    {
                        return (int)longValue;
                    }

                    if (value != null && int.TryParse(value.ToString(), out var parsed) && parsed > 0 && parsed <= 65535)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
            }

            return 3389;
        }

        private static void SetRdpPortInRegistry(string keyPath, int newPort)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(keyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException($"Registry key not found: {keyPath}");
                }

                key.SetValue("PortNumber", newPort, RegistryValueKind.DWord);
            }
        }

        private static void ConfigureAutoAdminLogon(string userName, string password)
        {
            using (var key = Registry.LocalMachine.CreateSubKey(WinlogonPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Could not open Winlogon registry key.");
                }

                var domainName = Environment.UserDomainName;
                if (string.IsNullOrWhiteSpace(domainName))
                {
                    domainName = Environment.MachineName;
                }

                key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
                key.SetValue("DefaultUserName", userName ?? string.Empty, RegistryValueKind.String);
                key.SetValue("DefaultUsername", userName ?? string.Empty, RegistryValueKind.String);
                key.SetValue("DefaultPassword", password ?? string.Empty, RegistryValueKind.String);
                key.SetValue("DefaultDomainName", domainName, RegistryValueKind.String);

                if (Array.IndexOf(key.GetValueNames(), "ForceAutoLogon") >= 0)
                {
                    key.DeleteValue("ForceAutoLogon", false);
                }
            }
        }

        private static string GetBestErrorText(ProcessResult result)
        {
            var error = (result.StandardError ?? string.Empty).Trim();
            var output = (result.StandardOutput ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }

            return "Unknown error.";
        }

        private async Task<OperationResult> EnsureFirewallRuleAsync(int port, string protocol)
        {
            var ruleName = $"BlockUpdateWindowsDefender-RDP-{protocol}-{port}";

            await _processRunner.RunAsync(
                "netsh.exe",
                $"advfirewall firewall delete rule name=\"{ruleName}\"");

            var addResult = await _processRunner.RunAsync(
                "netsh.exe",
                $"advfirewall firewall add rule name=\"{ruleName}\" profile=any dir=in action=allow protocol={protocol} localport={port}");

            if (!addResult.IsSuccess)
            {
                return new OperationResult
                {
                    IsSuccess = false,
                    Message = $"RDP port was changed, but could not add firewall rule ({protocol}/{port}): {GetBestErrorText(addResult)}"
                };
            }

            return new OperationResult
            {
                IsSuccess = true,
                Message = "OK"
            };
        }

        private async Task<OperationResult> ApplyRdpPortWithoutRebootAsync()
        {
            var restartResult = await _processRunner.RunAsync(
                "cmd.exe",
                "/c timeout /t 2 /nobreak >nul & net stop termservice /y & timeout /t 2 /nobreak >nul & net start termservice");

            var isRunning = await IsTermServiceRunningAsync();
            if (isRunning)
            {
                return new OperationResult
                {
                    IsSuccess = true,
                    Message = "TermService is running."
                };
            }

            for (var i = 0; i < 2; i++)
            {
                await _processRunner.RunAsync("cmd.exe", "/c net start termservice");
                await Task.Delay(1200);
                if (await IsTermServiceRunningAsync())
                {
                    return new OperationResult
                    {
                        IsSuccess = true,
                        Message = "TermService recovered after retry."
                    };
                }
            }

            return new OperationResult
            {
                IsSuccess = false,
                Message = GetBestErrorText(restartResult)
            };
        }

        private async Task<bool> IsTermServiceRunningAsync()
        {
            var queryResult = await _processRunner.RunAsync("sc.exe", "query termservice");
            if (!queryResult.IsSuccess)
            {
                return false;
            }

            var output = (queryResult.StandardOutput ?? string.Empty).ToUpperInvariant();
            return output.Contains("RUNNING");
        }
    }
}
