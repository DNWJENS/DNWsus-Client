using System;

namespace WindowsUpdateManager.Models
{
    public class InstallResult
    {
        public string ServerName { get; set; }
        public string KBArticleId { get; set; }
        public string UpdateTitle { get; set; }
        public double SizeMB { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime? RebootScheduledAt { get; set; }

        public InstallResult()
        {
            Timestamp = DateTime.Now;
        }
    }
}
