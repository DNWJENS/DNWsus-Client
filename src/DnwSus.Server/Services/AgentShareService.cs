using System.Security.AccessControl;
using System.Security.Principal;
using DnwSus.Server.Data;

namespace DnwSus.Server.Services;

/// <summary>
/// Hält die Agent-Verteilung ohne manuelles Zutun bereit:
///  1. legt beim Start einen öffentlich lesbaren Ordner + SMB-Freigabe <c>\\&lt;server&gt;\DNWsus</c> an (Jeder darf lesen,
///     schreiben nur SYSTEM/Administratoren),
///  2. kopiert die aktuelle Agent-MSI dort hinein – beim Start (also nach jedem Server-Update) und stündlich.
/// So muss pro Domäne nur noch eine GPO mit festem Pfad angelegt werden. Ein abweichender Zielpfad (z. B. ein zentraler
/// Dateiserver) kann im Dashboard gesetzt werden und hat dann Vorrang. Firewall-Regeln bleiben Sache des Administrators.
/// </summary>
public sealed class AgentShareService : BackgroundService
{
    /// <summary>Settings-Schlüssel für die zuletzt aufgetretene Freigabe-Fehlermeldung (leer = ok).</summary>
    public const string ShareErrorSetting = "AgentShareError";

    private readonly Database _db;
    private readonly AgentDistribution _dist;
    private readonly ILogger<AgentShareService> _log;

    public AgentShareService(Database db, AgentDistribution dist, ILogger<AgentShareService> log)
    {
        _db = db;
        _dist = dist;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stop); // Zertifikat/MSI nach dem Start bereitstehen lassen
            EnsureShare();
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

    /// <summary>Ordner + NTFS-Recht (Jeder lesen) + SMB-Freigabe anlegen. Fehler werden protokolliert und im Dashboard gezeigt.</summary>
    private void EnsureShare()
    {
        try
        {
            string folder = AgentDistribution.DefaultShareFolder;
            CreatePublicFolder(folder);
            WindowsShare.Ensure(AgentDistribution.ShareName, folder, "DNWsus – Agent-MSI (nur lesen)");
            _db.SetSetting(ShareErrorSetting, "");
            _log.LogInformation("Agent-Freigabe {Share} bereit ({Folder}).", AgentDistribution.ShareName, folder);
        }
        catch (Exception ex)
        {
            _db.SetSetting(ShareErrorSetting, ex.Message);
            _log.LogError(ex, "Agent-Freigabe konnte nicht angelegt werden");
        }
    }

    /// <summary>Aktuelle Agent-MSI veröffentlichen – abweichender Zielpfad (falls gesetzt) hat Vorrang vor der Freigabe.</summary>
    private void Publish()
    {
        try
        {
            string? overridePath = _db.GetSetting(AgentDistribution.TargetSettingKey);
            string target = string.IsNullOrWhiteSpace(overridePath) ? AgentDistribution.DefaultShareFolder : overridePath;

            var (ok, msg, copied) = _dist.PublishIfNewer(target);
            if (!ok)
                _log.LogWarning("Agent-MSI konnte nicht nach {Target} veröffentlicht werden: {Msg}", target, msg);
            else if (copied)
                _log.LogInformation("Agent-MSI veröffentlicht: {Msg}", msg);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fehler bei der automatischen Agent-Verteilung");
        }
    }

    /// <summary>Ordner anlegen und auf „Jeder lesen", SYSTEM/Administratoren Vollzugriff setzen (keine Vererbung).</summary>
    private static void CreatePublicFolder(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) dir.Create();

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);
    }
}
