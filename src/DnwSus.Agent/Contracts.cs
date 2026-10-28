using System.Collections.Generic;

namespace DnwSus.Agent
{
    // Austauschformat mit dem Server. Gegenstück: src/DnwSus.Server/Api/AgentContracts.cs – beide gemeinsam ändern.
    // Zeitangaben als String yyyy-MM-ddTHH:mm:ss (Ortszeit).

    public sealed class AgentReport
    {
        public string ComputerName { get; set; } = "";
        public string DnsName { get; set; } = "";
        public string IpAddresses { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string AgentVersion { get; set; } = "";
        public string BootTime { get; set; } = "";
        public string CurrentUser { get; set; } = "";
        public string LastUser { get; set; } = "";
        public string State { get; set; } = "";
        public string RebootPendingSince { get; set; } = "";
        public string LastError { get; set; } = "";
        public UpdateScan Scan { get; set; }
        public InstallReport Install { get; set; }
    }

    public sealed class UpdateScan
    {
        public string ScannedAt { get; set; } = "";
        public List<UpdateItem> Updates { get; set; } = new List<UpdateItem>();
    }

    public sealed class UpdateItem
    {
        public string KB { get; set; } = "";
        public string Title { get; set; } = "";
        public double SizeMB { get; set; }
        public bool RebootRequired { get; set; }
        public string Category { get; set; } = "";
    }

    public sealed class InstallReport
    {
        public List<long> RunIds { get; set; } = new List<long>();
        public bool Success { get; set; }
        public int InstalledCount { get; set; }
        public int FailedCount { get; set; }
        public bool RebootRequired { get; set; }
        public string Message { get; set; } = "";
        public List<InstallItem> Items { get; set; } = new List<InstallItem>();
    }

    public sealed class InstallItem
    {
        public string KB { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Success { get; set; }
        public string Result { get; set; } = "";
    }

    public sealed class AgentResponse
    {
        /// <summary>1..3 = Update-Gruppe, 0 = keine, -1 = Konflikt.</summary>
        public int Group { get; set; }
        public List<long> InstallRunIds { get; set; } = new List<long>();
        public List<InstallRun> InstallRuns { get; set; } = new List<InstallRun>();
        public bool ScanRequested { get; set; }
        public string ServerTime { get; set; } = "";

        /// <summary>Serverumzug: Adresse des neuen Servers (leer = kein Umzug).</summary>
        public string MoveToUrl { get; set; } = "";

        /// <summary>Serverumzug: SHA-256-Fingerabdruck des neuen Serverzertifikats.</summary>
        public string MoveToCertHash { get; set; } = "";
    }

    public sealed class InstallRun
    {
        public long Id { get; set; }
        public bool Manual { get; set; }
        public string RebootAt { get; set; } = "";
    }
}
