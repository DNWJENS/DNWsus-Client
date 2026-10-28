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

        /// <summary>Update-Klasse (Definition/Security/Quality/Feature/Driver/Product/Other) – vom Agenten bestimmt.</summary>
        public string Class { get; set; } = "";
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

        /// <summary>Klassen, von denen tatsächlich etwas installiert wurde – für die Neustart-Entscheidung.</summary>
        public List<string> InstalledClasses { get; set; } = new List<string>();
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

        /// <summary>Regeln pro Update-Klasse für die Gruppe dieses Clients (leer = alte Logik: alles bei einem Lauf).</summary>
        public List<ClassPolicy> Policy { get; set; } = new List<ClassPolicy>();
    }

    /// <summary>Regel für eine Update-Klasse: wann installiert, wie der Neustart gehandhabt wird.</summary>
    public sealed class ClassPolicy
    {
        public string Class { get; set; } = "";
        /// <summary>immediate = Agent installiert selbst · schedule = nur per Zeitplan-Lauf · manual = nur per Dashboard-Lauf · off = nie.</summary>
        public string Install { get; set; } = "";
        /// <summary>template = Neustart wie Gruppe · none = diese Klasse löst keinen erzwungenen Neustart aus.</summary>
        public string Reboot { get; set; } = "template";
    }

    public sealed class InstallRun
    {
        public long Id { get; set; }
        public bool Manual { get; set; }
        public string RebootAt { get; set; } = "";

        /// <summary>Kommagetrennte Klassen, die dieser Lauf installieren soll. Leer = alle (Abwärtskompatibilität).</summary>
        public string Classes { get; set; } = "";
    }
}
