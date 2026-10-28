namespace DnwSus.Server.Api;

// Austauschformat zwischen Agent und Server (JSON, Eigenschaftsnamen unverändert in PascalCase).
// Zeitangaben sind Strings im Format yyyy-MM-ddTHH:mm:ss (Ortszeit), weil der Agent (.NET Framework 4.6.2,
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

    /// <summary>Update-Klasse (Definition/Security/Quality/Feature/Driver/Product/Other), vom Agenten bestimmt.</summary>
    public string Class { get; set; } = "";
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

    /// <summary>Klassen, von denen tatsächlich etwas installiert wurde (für die Neustart-Entscheidung des Agenten).</summary>
    public List<string> InstalledClasses { get; set; } = new();
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

    /// <summary>Offene Läufe als reine IDs – für ältere Agenten (vor 2.1.9), die noch keine Reboot-Zeit kennen.</summary>
    public List<long> InstallRunIds { get; set; } = new();

    /// <summary>Offene Läufe mit Reboot-Zeit/Manuell-Kennung (ab Agent 2.1.9). Leer = nichts zu tun.</summary>
    public List<InstallRun> InstallRuns { get; set; } = new();

    /// <summary>Im Dashboard wurde ein neuer Scan angefordert.</summary>
    public bool ScanRequested { get; set; }

    public string ServerTime { get; set; } = "";

    /// <summary>Serverumzug: Adresse des neuen Servers. Gesetzt = der Agent soll künftig dorthin melden. Leer = kein Umzug.</summary>
    public string MoveToUrl { get; set; } = "";

    /// <summary>Serverumzug: SHA-256-Fingerabdruck des Zertifikats des neuen Servers (der Agent übernimmt nur bei Übereinstimmung).</summary>
    public string MoveToCertHash { get; set; } = "";

    /// <summary>Regeln pro Update-Klasse für die Gruppe dieses Clients. Leer = alte Logik (alles bei einem Lauf).</summary>
    public List<ClassPolicy> Policy { get; set; } = new();
}

/// <summary>Regel für eine Update-Klasse: wann installiert wird und wie der Neustart gehandhabt wird.</summary>
public sealed class ClassPolicy
{
    public string Class { get; set; } = "";

    /// <summary>immediate = Agent installiert selbst · schedule = nur per Zeitplan · manual = nur per Dashboard-Lauf · off = nie.</summary>
    public string Install { get; set; } = "";

    /// <summary>template = Neustart wie Gruppe · none = diese Klasse löst keinen erzwungenen Neustart aus.</summary>
    public string Reboot { get; set; } = "template";
}

public sealed class InstallRun
{
    public long Id { get; set; }

    /// <summary>Manueller Soforthilfe-Lauf: übersteuert das Gruppen-Neustart-Verhalten.</summary>
    public bool Manual { get; set; }

    /// <summary>Geplanter Neustart-Zeitpunkt (nur manuelle Läufe, nur falls Neustart nötig). Leer = kein erzwungener Neustart.</summary>
    public string RebootAt { get; set; } = "";

    /// <summary>Kommagetrennte Klassen, die dieser Lauf installieren soll. Leer = alle (Abwärtskompatibilität).</summary>
    public string Classes { get; set; } = "";
}
