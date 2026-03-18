namespace BlockUpdateWindowsDefender.Models
{
    public class SystemInfoModel
    {
        public string ComputerName { get; set; }
        public string Caption { get; set; }
        public string Version { get; set; }
        public string BuildNumber { get; set; }
        public string Architecture { get; set; }

        public string DisplayVersion => $"{Version} / Build {BuildNumber} / {Architecture}";
    }
}
