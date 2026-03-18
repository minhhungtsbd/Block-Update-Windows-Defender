namespace BlockUpdateWindowsDefender.Services
{
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool IsSuccess => ExitCode == 0;
    }
}
