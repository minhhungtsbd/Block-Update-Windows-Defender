using System;
using System.Collections.Generic;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading.Tasks;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class TimeSyncService
    {
        private readonly ProcessRunner _processRunner;

        public TimeSyncService(ProcessRunner processRunner)
        {
            _processRunner = processRunner;
        }

        public async Task<TimeSyncResult> SyncAsync()
        {
            var timezoneMessage = "Time zone unchanged.";
            EnsureWindowsTimeServiceRunning();

            var resyncResult = await _processRunner.RunAsync("w32tm.exe", "/resync /force");
            if (!resyncResult.IsSuccess)
            {
                return new TimeSyncResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(resyncResult.StandardError)
                        ? resyncResult.StandardOutput.Trim()
                        : resyncResult.StandardError.Trim(),
                    TimeZoneDisplayName = TimeZoneInfo.Local.DisplayName,
                    LocalTime = DateTime.Now
                };
            }

            var detectedTimeZone = await DetectTimeZoneAsync();
            if (!string.IsNullOrWhiteSpace(detectedTimeZone))
            {
                var setTimeZoneResult = await _processRunner.RunAsync("tzutil.exe", $"/s \"{detectedTimeZone}\"");
                timezoneMessage = setTimeZoneResult.IsSuccess
                    ? $"Time zone updated to {detectedTimeZone}."
                    : "Could not update time zone automatically.";
            }
            else
            {
                timezoneMessage = "Could not map the detected IP time zone. Time zone was left unchanged.";
            }

            return new TimeSyncResult
            {
                Success = true,
                Message = $"Windows time synchronized successfully. {timezoneMessage}",
                TimeZoneDisplayName = TimeZoneInfo.Local.DisplayName,
                LocalTime = DateTime.Now
            };
        }

        private static void EnsureWindowsTimeServiceRunning()
        {
            using (var service = new ServiceController("w32time"))
            {
                if (service.Status == ServiceControllerStatus.Running)
                {
                    return;
                }

                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
        }

        private static async Task<string> DetectTimeZoneAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockUpdateWindowsDefender/1.0");
                    var ianaTimeZone = (await client.GetStringAsync("https://ipapi.co/timezone/")).Trim();
                    if (string.IsNullOrWhiteSpace(ianaTimeZone))
                    {
                        return null;
                    }

                    return MapIanaToWindows(ianaTimeZone);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string MapIanaToWindows(string ianaTimeZone)
        {
            string windowsTimeZone;
            return TimeZoneMappings.TryGetValue(ianaTimeZone, out windowsTimeZone)
                ? windowsTimeZone
                : null;
        }

        private static readonly Dictionary<string, string> TimeZoneMappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" },
                { "Asia/Bangkok", "SE Asia Standard Time" },
                { "Asia/Jakarta", "SE Asia Standard Time" },
                { "Asia/Singapore", "Singapore Standard Time" },
                { "Asia/Kuala_Lumpur", "Singapore Standard Time" },
                { "Asia/Manila", "Singapore Standard Time" },
                { "Asia/Tokyo", "Tokyo Standard Time" },
                { "Asia/Seoul", "Korea Standard Time" },
                { "Asia/Shanghai", "China Standard Time" },
                { "Asia/Hong_Kong", "China Standard Time" },
                { "Asia/Taipei", "Taipei Standard Time" },
                { "Asia/Kolkata", "India Standard Time" },
                { "Asia/Dubai", "Arabian Standard Time" },
                { "Asia/Riyadh", "Arab Standard Time" },
                { "Asia/Jerusalem", "Israel Standard Time" },
                { "Europe/London", "GMT Standard Time" },
                { "Europe/Berlin", "W. Europe Standard Time" },
                { "Europe/Paris", "Romance Standard Time" },
                { "Europe/Madrid", "Romance Standard Time" },
                { "Europe/Rome", "W. Europe Standard Time" },
                { "Europe/Warsaw", "Central European Standard Time" },
                { "Europe/Bucharest", "GTB Standard Time" },
                { "Europe/Athens", "GTB Standard Time" },
                { "Europe/Helsinki", "FLE Standard Time" },
                { "Europe/Moscow", "Russian Standard Time" },
                { "Africa/Cairo", "Egypt Standard Time" },
                { "Africa/Johannesburg", "South Africa Standard Time" },
                { "Australia/Perth", "W. Australia Standard Time" },
                { "Australia/Adelaide", "Cen. Australia Standard Time" },
                { "Australia/Sydney", "AUS Eastern Standard Time" },
                { "Pacific/Auckland", "New Zealand Standard Time" },
                { "Pacific/Fiji", "Fiji Standard Time" },
                { "America/Anchorage", "Alaskan Standard Time" },
                { "America/Los_Angeles", "Pacific Standard Time" },
                { "America/Denver", "Mountain Standard Time" },
                { "America/Phoenix", "US Mountain Standard Time" },
                { "America/Chicago", "Central Standard Time" },
                { "America/New_York", "Eastern Standard Time" },
                { "America/Toronto", "Eastern Standard Time" },
                { "America/Halifax", "Atlantic Standard Time" },
                { "America/St_Johns", "Newfoundland Standard Time" },
                { "America/Sao_Paulo", "E. South America Standard Time" },
                { "America/Buenos_Aires", "Argentina Standard Time" },
                { "America/Mexico_City", "Central Standard Time (Mexico)" }
            };
    }
}
