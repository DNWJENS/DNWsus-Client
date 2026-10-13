using System;
using System.Collections.Generic;

namespace WindowsUpdateManager.Models
{
    public enum ServerStatus { Unknown, Online, Offline, Scanning, Installing, Scheduled, Done, Error }

    public class ServerInfo
    {
        private List<UpdateInfo> _availableUpdates = new List<UpdateInfo>();

        public string Name { get; set; }
        public string OperatingSystem { get; set; }
        public string OU { get; set; }
        public ServerStatus Status { get; set; }
        public bool Selected { get; set; }
        public DateTime? RebootScheduledAt { get; set; }
        public string ErrorMessage { get; set; }
        public string StatusMessage { get; set; }
        public int InstallProgress { get; set; }
        public string Uptime { get; set; }
        public DateTime? InstallScheduledAt { get; set; }

        public List<UpdateInfo> AvailableUpdates
        {
            get { return _availableUpdates; }
            set { _availableUpdates = value ?? new List<UpdateInfo>(); }
        }

        public bool RebootRequired
        {
            get { return _availableUpdates.Exists(u => u.RebootRequired); }
        }

        public ServerInfo()
        {
            Status   = ServerStatus.Unknown;
            Selected = true;
        }
    }
}
