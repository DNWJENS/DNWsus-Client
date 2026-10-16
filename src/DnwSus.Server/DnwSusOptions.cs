namespace DnwSus.Server;

/// <summary>Einstellungen aus appsettings.json, Abschnitt "DnwSus".</summary>
public sealed class DnwSusOptions
{
    /// <summary>HTTPS-Port für Dashboard, Agent-Schnittstelle und MSI-Download.</summary>
    public int Port { get; set; } = 13162;

    public static readonly string[] DefaultGroups = { "GRP_Update1", "GRP_Update2", "GRP_Update3" };

    /// <summary>
    /// Die drei Update-Gruppen (sAMAccountName), Reihenfolge = GRP_Update1..3.
    /// Bewusst leer vorbelegt: Der Konfigurations-Binder hängt Listen aus appsettings.json an eine
    /// vorhandene Liste an, statt sie zu ersetzen. Standardwerte setzt <see cref="Normalize"/>.
    /// </summary>
    public List<string> Groups { get; set; } = new();

    /// <summary>
    /// AD-Gruppe (sAMAccountName), deren Mitglieder das Dashboard nutzen dürfen.
    /// Leer = Domänen-Admins der Domäne des Servers.
    /// </summary>
    public string AdminGroup { get; set; } = "";

    /// <summary>Clients ohne AD-Anmeldung seit so vielen Tagen gelten als veraltet (grau).</summary>
    public int StaleDays { get; set; } = 90;

    /// <summary>So lange wird das Ergebnis der AD-Abfrage zwischengespeichert.</summary>
    public int AdCacheMinutes { get; set; } = 5;

    /// <summary>Leere/doppelte Gruppennamen entfernen, höchstens drei Gruppen, sonst Standardwerte.</summary>
    public void Normalize()
    {
        Groups = Groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        if (Groups.Count == 0) Groups = DefaultGroups.ToList();
    }
}
