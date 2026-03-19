using System;
using System.Collections.Generic;
using System.Net.Http;
using System.ServiceProcess;
using System.Text.RegularExpressions;
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

        public async Task<TimeSyncResult> SyncAsync(
            string currentLanguageCode = null,
            bool autoDetectTimeZone = true,
            string manualWindowsTimeZoneId = null)
        {
            var fallbackLanguageCode = NormalizeLanguageCode(currentLanguageCode);
            var timezoneMessage = IsVietnameseLanguage(fallbackLanguageCode)
                ? "Múi giờ được giữ nguyên."
                : "Time zone unchanged.";
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
                    LocalTime = DateTime.Now,
                    SuggestedLanguageCode = fallbackLanguageCode
                };
            }

            if (!autoDetectTimeZone)
            {
                string manualMessage;
                if (!string.IsNullOrWhiteSpace(manualWindowsTimeZoneId))
                {
                    var setTimeZoneResult = await _processRunner.RunAsync("tzutil.exe", $"/s \"{manualWindowsTimeZoneId}\"");
                    manualMessage = setTimeZoneResult.IsSuccess
                        ? (IsVietnameseLanguage(fallbackLanguageCode)
                            ? $"\u0110\u1ED3ng b\u1ED9 gi\u1EDD Windows th\u00E0nh c\u00F4ng. M\u00FAi gi\u1EDD th\u1EE7 c\u00F4ng \u0111\u00E3 \u0111\u01B0\u1EE3c c\u1EADp nh\u1EADt sang {manualWindowsTimeZoneId}."
                            : $"Windows time synchronized successfully. Manual time zone updated to {manualWindowsTimeZoneId}.")
                        : (IsVietnameseLanguage(fallbackLanguageCode)
                            ? $"\u0110\u1ED3ng b\u1ED9 gi\u1EDD Windows th\u00E0nh c\u00F4ng nh\u01B0ng kh\u00F4ng th\u1EC3 c\u1EADp nh\u1EADt m\u00FAi gi\u1EDD th\u1EE7 c\u00F4ng ({manualWindowsTimeZoneId})."
                            : $"Windows time synchronized successfully but could not apply manual time zone ({manualWindowsTimeZoneId}).");
                }
                else
                {
                    manualMessage = IsVietnameseLanguage(fallbackLanguageCode)
                        ? "\u0110\u1ED3ng b\u1ED9 gi\u1EDD Windows th\u00E0nh c\u00F4ng. Ch\u1EBF \u0111\u1ED9 c\u1EADp nh\u1EADt th\u1EE7 c\u00F4ng \u0111ang b\u1EADt. M\u00FAi gi\u1EDD \u0111\u01B0\u1EE3c gi\u1EEF nguy\u00EAn."
                        : "Windows time synchronized successfully. Manual update time & time zone mode is active. Time zone was left unchanged.";
                }

                return new TimeSyncResult
                {
                    Success = true,
                    Message = manualMessage,
                    TimeZoneDisplayName = TimeZoneInfo.Local.DisplayName,
                    LocalTime = DateTime.Now,
                    SuggestedLanguageCode = fallbackLanguageCode
                };
            }

            var detectedResult = await DetectTimeZoneAsync();
            var detectedIanaTimeZone = detectedResult != null ? detectedResult.IanaTimeZone : null;
            var suggestedLanguageCode = ResolveSuggestedLanguageCode(detectedResult, fallbackLanguageCode);
            var useVietnamese = IsVietnameseLanguage(suggestedLanguageCode);
            var detectedWindowsTimeZone = MapIanaToWindows(detectedIanaTimeZone);
            if (!string.IsNullOrWhiteSpace(detectedWindowsTimeZone))
            {
                var setTimeZoneResult = await _processRunner.RunAsync("tzutil.exe", $"/s \"{detectedWindowsTimeZone}\"");
                timezoneMessage = setTimeZoneResult.IsSuccess
                    ? (useVietnamese
                        ? $"Múi giờ đã được cập nhật sang {detectedWindowsTimeZone}."
                        : $"Time zone updated to {detectedWindowsTimeZone}.")
                    : (useVietnamese
                        ? "Không thể tự động cập nhật múi giờ."
                        : "Could not update time zone automatically.");
            }
            else
            {
                timezoneMessage = string.IsNullOrWhiteSpace(detectedIanaTimeZone)
                    ? (useVietnamese
                        ? "Không thể nhận diện múi giờ IP từ các dịch vụ online. Múi giờ được giữ nguyên."
                        : "Could not detect IP time zone from online services. Time zone was left unchanged.")
                    : (useVietnamese
                        ? $"Không map được múi giờ IP ({detectedIanaTimeZone}). Múi giờ được giữ nguyên."
                        : $"Could not map detected IP time zone ({detectedIanaTimeZone}). Time zone was left unchanged.");
            }

            return new TimeSyncResult
            {
                Success = true,
                Message = useVietnamese
                    ? $"Đồng bộ giờ Windows thành công. {timezoneMessage}"
                    : $"Windows time synchronized successfully. {timezoneMessage}",
                TimeZoneDisplayName = TimeZoneInfo.Local.DisplayName,
                LocalTime = DateTime.Now,
                SuggestedLanguageCode = suggestedLanguageCode,
                DetectedPublicIp = detectedResult?.PublicIp
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

        private static async Task<TimeZoneDetectionResult> DetectTimeZoneAsync()
        {
            var endpoints = new[]
            {
                "https://ipwho.is/",
                "https://free.freeipapi.com/api/json/",
                "http://ip-api.com/json/"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockUpdateWindowsDefender/1.0");
                        var response = (await client.GetStringAsync(endpoint)).Trim();
                        var detected = ExtractTimeZone(endpoint, response);
                        if (detected != null && !string.IsNullOrWhiteSpace(detected.IanaTimeZone))
                        {
                            return detected;
                        }

                        if (LooksLikeIanaTimeZone(response))
                        {
                            return new TimeZoneDetectionResult
                            {
                                IanaTimeZone = response.Trim().Replace("\\/", "/")
                            };
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static TimeZoneDetectionResult ExtractTimeZone(string endpoint, string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            var host = GetEndpointHost(endpoint);
            if (string.Equals(host, "ipapi.co", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "www.ipapi.co", StringComparison.OrdinalIgnoreCase))
            {
                return new TimeZoneDetectionResult
                {
                    IanaTimeZone = response.Trim().Replace("\\/", "/")
                };
            }

            if (string.Equals(host, "free.freeipapi.com", StringComparison.OrdinalIgnoreCase))
            {
                var freeIpApiMatch = Regex.Match(response, "\"timeZones\"\\s*:\\s*\\[\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var countryCodeMatch = Regex.Match(response, "\"countryCode\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var ipMatch = Regex.Match(response, "\"ipAddress\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (freeIpApiMatch.Success || countryCodeMatch.Success)
                {
                    return new TimeZoneDetectionResult
                    {
                        IanaTimeZone = freeIpApiMatch.Success
                            ? freeIpApiMatch.Groups[1].Value.Trim().Replace("\\/", "/")
                            : null,
                        CountryCode = countryCodeMatch.Success
                            ? countryCodeMatch.Groups[1].Value.Trim().ToUpperInvariant()
                            : null,
                        PublicIp = ipMatch.Success
                            ? ipMatch.Groups[1].Value.Trim()
                            : null
                    };
                }
            }

            if (string.Equals(host, "ipwho.is", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "www.ipwho.is", StringComparison.OrdinalIgnoreCase))
            {
                var ipWhoIsMatch = Regex.Match(response, "\"id\"\\s*:\\s*\"([^\"]+/[^\"]+)\"", RegexOptions.IgnoreCase);
                var countryCodeMatch = Regex.Match(response, "\"country_code\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var ipMatch = Regex.Match(response, "\"ip\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (ipWhoIsMatch.Success || countryCodeMatch.Success)
                {
                    return new TimeZoneDetectionResult
                    {
                        IanaTimeZone = ipWhoIsMatch.Success
                            ? ipWhoIsMatch.Groups[1].Value.Trim().Replace("\\/", "/")
                            : null,
                        CountryCode = countryCodeMatch.Success
                            ? countryCodeMatch.Groups[1].Value.Trim().ToUpperInvariant()
                            : null,
                        PublicIp = ipMatch.Success
                            ? ipMatch.Groups[1].Value.Trim()
                            : null
                    };
                }
            }

            if (string.Equals(host, "ip-api.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "www.ip-api.com", StringComparison.OrdinalIgnoreCase))
            {
                var ipApiMatch = Regex.Match(response, "\"timezone\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var countryCodeMatch = Regex.Match(response, "\"countryCode\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var ipMatch = Regex.Match(response, "\"query\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (ipApiMatch.Success || countryCodeMatch.Success)
                {
                    return new TimeZoneDetectionResult
                    {
                        IanaTimeZone = ipApiMatch.Success
                            ? ipApiMatch.Groups[1].Value.Trim().Replace("\\/", "/")
                            : null,
                        CountryCode = countryCodeMatch.Success
                            ? countryCodeMatch.Groups[1].Value.Trim().ToUpperInvariant()
                            : null,
                        PublicIp = ipMatch.Success
                            ? ipMatch.Groups[1].Value.Trim()
                            : null
                    };
                }
            }

            return null;
        }

        private static string ResolveSuggestedLanguageCode(TimeZoneDetectionResult detectedResult, string fallbackLanguageCode)
        {
            if (detectedResult == null)
            {
                return NormalizeLanguageCode(fallbackLanguageCode);
            }

            if (string.Equals(detectedResult.CountryCode, "VN", StringComparison.OrdinalIgnoreCase))
            {
                return "vi";
            }

            if (string.Equals(detectedResult.IanaTimeZone, "Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(detectedResult.IanaTimeZone, "Asia/Saigon", StringComparison.OrdinalIgnoreCase))
            {
                return "vi";
            }

            return "en";
        }

        private static bool IsVietnameseLanguage(string languageCode)
        {
            return NormalizeLanguageCode(languageCode) == "vi";
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return "en";
            }

            return languageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                ? "vi"
                : "en";
        }

        private static string GetEndpointHost(string endpoint)
        {
            Uri uri;
            return Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
                ? uri.Host
                : string.Empty;
        }

        private static bool LooksLikeIanaTimeZone(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf('/') > 0 &&
                   value.IndexOf(' ') < 0 &&
                   value.IndexOf('{') < 0;
        }

        private static string MapIanaToWindows(string ianaTimeZone)
        {
            if (string.IsNullOrWhiteSpace(ianaTimeZone))
            {
                return null;
            }

            string windowsTimeZone;
            return TimeZoneMappings.TryGetValue(ianaTimeZone, out windowsTimeZone)
                ? windowsTimeZone
                : null;
        }

        private class TimeZoneDetectionResult
        {
            public string IanaTimeZone { get; set; }
            public string CountryCode { get; set; }
            public string PublicIp { get; set; }
        }

        private static readonly Dictionary<string, string> TimeZoneMappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" },
                { "Asia/Bangkok", "SE Asia Standard Time" },
                { "Asia/Jakarta", "SE Asia Standard Time" },
                { "Asia/Saigon", "SE Asia Standard Time" },
                { "Asia/Singapore", "Singapore Standard Time" },
                { "Asia/Kuala_Lumpur", "Singapore Standard Time" },
                { "Asia/Manila", "Singapore Standard Time" },
                { "Asia/Tokyo", "Tokyo Standard Time" },
                { "Asia/Seoul", "Korea Standard Time" },
                { "Asia/Shanghai", "China Standard Time" },
                { "Asia/Chongqing", "China Standard Time" },
                { "Asia/Chungking", "China Standard Time" },
                { "Asia/Harbin", "China Standard Time" },
                { "Asia/Urumqi", "China Standard Time" },
                { "Asia/Hong_Kong", "China Standard Time" },
                { "Asia/Taipei", "Taipei Standard Time" },
                { "Asia/Kolkata", "India Standard Time" },
                { "Asia/Calcutta", "India Standard Time" },
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
