namespace BlockUpdateWindowsDefender.Models
{
    public class DefenderStatus
    {
        public bool IsSupported { get; set; }
        public bool IsRealtimeProtectionEnabled { get; set; }
        public bool IsAntivirusEnabled { get; set; }
        public bool IsTamperProtected { get; set; }
        public string StatusText { get; set; }
        public string DetailText { get; set; }
    }
}
