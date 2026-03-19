using System.Collections.Generic;

namespace BlockUpdateWindowsDefender.Models
{
    public class RdpHistoryResult
    {
        public bool IsSuccess { get; set; }
        public string Source { get; set; }
        public string ErrorMessage { get; set; }
        public List<RdpLoginRecord> Records { get; set; } = new List<RdpLoginRecord>();
    }
}
