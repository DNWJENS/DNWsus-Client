namespace DnwSus.Server.Api;

// Austauschformat zwischen Agent und Server (JSON, Eigenschaftsnamen unverändert in PascalCase).
// Zeitangaben sind Strings im Format yyyy-MM-ddTHH:mm:ss (Ortszeit), weil der Agent (.NET 4.8,
// JavaScriptSerializer) DateTime sonst in einem eigenen Format schreibt.
// Gegenstück im Agenten: src/DnwSus.Agent/Contracts.cs – beide Dateien gemeinsam ändern.

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

    /// <summary>Bereit, Sucht, Installiert, Neustart ausstehend …</summary>
    public string State { get; set; } = "";
    public string RebootPendingSince { get; set; } = "";
    public string LastError { get; set; } = "";

    /// <summary>Nur gesetzt, wenn seit der letzten Meldung neu gescannt wurde.</summary>
    public UpdateScan? Scan { get; set; }

    /// <summary>Nur gesetzt, wenn seit der letzten Meldung eine Installation abgeschlossen wurde.</summary>
    public InstallReport? Install { get; set; }
}

public sealed class UpdateScan
{
    public string ScannedAt { get; set; } = "";
    public List<UpdateItem> Updates { get; set; } = new();
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
    public List<long> RunIds { get; set; } = new();
    public bool Success { get; set; }
    public int InstalledCount { get; set; }
    public int FailedCount { get; set; }
    public bool RebootRequired { get; set; }
    public string Message { get; set; } = "";
    public List<InstallItem> Items { get; set; } = new();
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
    /// <summary>Update-Gruppe des Clients: 1..3, 0 = keine, -1 = Konflikt (mehrere Gruppen).</summary>
    public int Group { get; set; }

    /// <summary>Offene Läufe, die der Agent jetzt installieren soll (leer = nichts zu tun).</summary>
    public List<long> InstallRunIds { get; set; } = new();

    /// <summary>Im Dashboard wurde ein neuer Scan angefordert.</summary>
    public bool ScanRequested { get; set; }

    public string ServerTime { get; set; } = "";
}
