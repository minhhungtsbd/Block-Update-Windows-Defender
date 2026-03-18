using System;

namespace BlockUpdateWindowsDefender.Models
{
    public class TimeSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string TimeZoneDisplayName { get; set; }
        public DateTime LocalTime { get; set; }
        public string SuggestedLanguageCode { get; set; }
        public string DetectedPublicIp { get; set; }
    }
}
