namespace BlockUpdateWindowsDefender.Models
{
    public class WindowsUpdateStatus
    {
        public bool IsEnabled { get; set; }
        public string StatusText { get; set; }
        public string DetailText { get; set; }
        public string ServiceState { get; set; }
    }
}
