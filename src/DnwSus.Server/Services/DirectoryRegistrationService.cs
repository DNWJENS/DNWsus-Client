using System.DirectoryServices;

namespace DnwSus.Server.Services;

/// <summary>
/// Veröffentlicht einen Service Connection Point (SCP) im AD unter dem Computerkonto dieses Servers, damit Werkzeuge
/// (z. B. die DNWironment-Konsole) per LDAP erkennen können, dass in dieser Domäne bereits ein DNWsus-Server läuft –
/// ganz ohne Portscan. Das Computerkonto darf SCP-Kindobjekte unter sich selbst anlegen; der Dienst läuft als SYSTEM.
/// Läuft beim Start und stündlich zur Selbstheilung. Fehler sind nicht fatal (nur protokolliert).
/// </summary>
public sealed class DirectoryRegistrationService : BackgroundService
{
    /// <summary>Schlüsselwort, nach dem die Konsole sucht, um DNWsus-Server zu finden.</summary>
    public const string Keyword = "DNWsus-Server";
    private const string ScpName = "CN=DNWsus";

    private readonly ServerIdentity _identity;
    private readonly ILogger<DirectoryRegistrationService> _log;

    public DirectoryRegistrationService(ServerIdentity identity, ILogger<DirectoryRegistrationService> log)
    {
        _identity = identity;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
            do
            {
                Publish();
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
            // Dienst wird beendet – kein Fehler.
        }
    }

    private void Publish()
    {
        try
        {
            string? computerDn = FindOwnComputerDn();
            if (computerDn == null)
            {
                _log.LogWarning("Eigenes Computerkonto im AD nicht gefunden – SCP wird nicht veröffentlicht.");
                return;
            }

            using var computer = new DirectoryEntry("LDAP://" + computerDn);
            DirectoryEntry scp;
            try
            {
                scp = computer.Children.Find(ScpName, "serviceConnectionPoint");
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                scp = computer.Children.Add(ScpName, "serviceConnectionPoint");
            }

            using (scp)
            {
                scp.Properties["keywords"].Value = Keyword;
                scp.Properties["serviceClassName"].Value = "DNWsus";
                scp.Properties["serviceBindingInformation"].Value = _identity.Url;       // https://fqdn:13162/
                scp.Properties["serviceDNSName"].Value = new Uri(_identity.Url).Host;    // fqdn
                scp.Properties["serviceDNSNameType"].Value = "A";
                scp.CommitChanges();
            }
            _log.LogInformation("DNWsus im AD registriert (SCP unter {Dn}).", computerDn);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SCP konnte nicht veröffentlicht werden (fehlende Rechte?).");
        }
    }

    private static string? FindOwnComputerDn()
    {
        using var rootDse = new DirectoryEntry("LDAP://RootDSE");
        if (rootDse.Properties["defaultNamingContext"].Value is not string namingContext) return null;

        using var root = new DirectoryEntry("LDAP://" + namingContext);
        using var searcher = new DirectorySearcher(root,
            "(&(objectCategory=computer)(sAMAccountName=" + Environment.MachineName + "$))",
            new[] { "distinguishedName" });
        var result = searcher.FindOne();
        return result?.Properties["distinguishedName"].Count > 0
            ? result.Properties["distinguishedName"][0]?.ToString()
            : null;
    }
}
