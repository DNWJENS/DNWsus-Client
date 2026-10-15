namespace DnwSus.Server;

/// <summary>Einstellungen aus appsettings.json, Abschnitt "DnwSus".</summary>
public sealed class DnwSusOptions
{
    /// <summary>HTTPS-Port für Dashboard, Agent-Schnittstelle und MSI-Download.</summary>
    public int Port { get; set; } = 13162;

    /// <summary>Die drei Update-Gruppen (sAMAccountName), Reihenfolge = GRP_Update1..3.</summary>
    public List<string> Groups { get; set; } = new() { "GRP_Update1", "GRP_Update2", "GRP_Update3" };

    /// <summary>
    /// AD-Gruppe (sAMAccountName), deren Mitglieder das Dashboard nutzen dürfen.
    /// Leer = Domänen-Admins der Domäne des Servers.
    /// </summary>
    public string AdminGroup { get; set; } = "";

    /// <summary>Clients ohne AD-Anmeldung seit so vielen Tagen gelten als veraltet (grau).</summary>
    public int StaleDays { get; set; } = 90;

    /// <summary>So lange wird das Ergebnis der AD-Abfrage zwischengespeichert.</summary>
    public int AdCacheMinutes { get; set; } = 5;
}
