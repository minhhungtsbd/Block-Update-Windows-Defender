namespace BlockUpdateWindowsDefender.Models
{
    public class WindowsUpdateStatus
    {
        public bool IsEnabled { get; set; }
        public string StatusText { get; set; }
        public string DetailText { get; set; }
        public string ServiceState { get; set; }
        public string PolicyStateText { get; set; }
        public string WuauservStateText { get; set; }
        public string UsoSvcStateText { get; set; }
        public string WaaSMedicSvcStateText { get; set; }
        public string ManualUpdateCapabilityText { get; set; }
    }
}
