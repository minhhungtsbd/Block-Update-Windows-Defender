using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class DefenderService
    {
        private readonly ProcessRunner _processRunner;

        public DefenderService(ProcessRunner processRunner)
        {
            _processRunner = processRunner;
        }

        public async Task<DefenderStatus> GetStatusAsync()
        {
            var script = @"
$cmd = Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue
if (-not $cmd) {
  Write-Output 'Supported=False'
  exit 0
}

try {
  $status = Get-MpComputerStatus
  Write-Output 'Supported=True'
  Write-Output ('RealtimeEnabled=' + $status.RealTimeProtectionEnabled)
  Write-Output ('AntivirusEnabled=' + $status.AntivirusEnabled)
  Write-Output ('TamperProtected=' + $status.IsTamperProtected)
}
catch {
  Write-Output 'Supported=False'
  Write-Output ('Error=' + $_.Exception.Message)
}";

            var result = await _processRunner.RunPowerShellAsync(script);
            var values = ParseKeyValues(result.StandardOutput);

            var isSupported = values.TryGetValue("Supported", out var supportedValue) &&
                              string.Equals(supportedValue, "True", StringComparison.OrdinalIgnoreCase);

            if (!isSupported)
            {
                var error = values.TryGetValue("Error", out var errorValue)
                    ? errorValue
                    : "Microsoft Defender is not available on this Windows version.";

                return new DefenderStatus
                {
                    IsSupported = false,
                    StatusText = "Unsupported",
                    DetailText = error
                };
            }

            var realtimeEnabled = GetBoolean(values, "RealtimeEnabled");
            var tamperProtected = GetBoolean(values, "TamperProtected");
            var detail = tamperProtected
                ? "Tamper Protection is enabled. Some changes can be blocked by Windows."
                : "Microsoft Defender is available and can be managed by this app.";

            return new DefenderStatus
            {
                IsSupported = true,
                IsRealtimeProtectionEnabled = realtimeEnabled,
                IsAntivirusEnabled = GetBoolean(values, "AntivirusEnabled"),
                IsTamperProtected = tamperProtected,
                StatusText = realtimeEnabled ? "Enabled" : "Disabled",
                DetailText = detail
            };
        }

        public async Task<DefenderStatus> SetRealtimeProtectionAsync(bool enabled)
        {
            var script = enabled
                ? "Set-MpPreference -DisableRealtimeMonitoring $false"
                : "Set-MpPreference -DisableRealtimeMonitoring $true";

            var result = await _processRunner.RunPowerShellAsync(script);
            if (!result.IsSuccess)
            {
                return new DefenderStatus
                {
                    IsSupported = true,
                    StatusText = "Action Failed",
                    DetailText = string.IsNullOrWhiteSpace(result.StandardError)
                        ? "Windows blocked the action."
                        : result.StandardError.Trim()
                };
            }

            return await GetStatusAsync();
        }

        private static Dictionary<string, string> ParseKeyValues(string output)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(output))
            {
                return values;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separatorIndex).Trim();
                var value = line.Substring(separatorIndex + 1).Trim();
                values[key] = value;
            }

            return values;
        }

        private static bool GetBoolean(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) &&
                   string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }
    }
}
