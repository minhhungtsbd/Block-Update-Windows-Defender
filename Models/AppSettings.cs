namespace BlockUpdateWindowsDefender.Models
{
    public class AppSettings
    {
        public bool AutoSyncTimeOnStartup { get; set; } = true;
        public bool AutoDetectTimeZoneFromIp { get; set; } = true;
        public bool AutoUpdateAppOnStartup { get; set; } = true;
        public bool AutoExtendSystemDriveOnStartup { get; set; }
        public bool RunAtWindowsStartup { get; set; }
        public string ManualTimeZoneId { get; set; }
        public string UiLanguageCode { get; set; } = "en";
        public string LastDetectedPublicIp { get; set; }
    }
}
