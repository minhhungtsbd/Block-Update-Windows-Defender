using System;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading.Tasks;
using BlockUpdateWindowsDefender.Models;
using TimeZoneConverter;

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

                    return TZConvert.IanaToWindows(ianaTimeZone);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
