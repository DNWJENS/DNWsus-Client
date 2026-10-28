using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

public sealed class AgentModel : PageModel
{
    private readonly AgentMsiService _msi;
    private readonly Database _db;
    private readonly AgentDistribution _dist;

    public AgentModel(AgentMsiService msi, ServerIdentity identity, Database db, AgentDistribution dist)
    {
        _msi = msi;
        _db = db;
        _dist = dist;
        Identity = identity;
    }

    public ServerIdentity Identity { get; }
    public bool TemplateExists => _msi.TemplateExists;
    public string AgentVersion { get; private set; } = "";
    public int Reporting { get; private set; }
    public int ReportingToday { get; private set; }
    public List<EventEntry> RecentEvents { get; private set; } = new();

    /// <summary>Lokaler Pfad der fertigen MSI auf dem Server (zum Kopieren ohne Browser-Download).</summary>
    public string? MsiPath { get; private set; }
    public string? MsiError { get; private set; }

    /// <summary>Server-eigene Freigabe: der fertige GPO-Pfad, den man nur noch in die GPO eintragen muss.</summary>
    public string ShareUnc { get; private set; } = "";
    public string? ShareError { get; private set; }

    /// <summary>Optionaler abweichender Zielpfad (leer = Server-eigene Freigabe wird genutzt).</summary>
    public string TargetPath { get; private set; } = "";

    /// <summary>Serverumzug: hinterlegte Adresse/Zertifikat des neuen Servers (leer = kein Umzug aktiv).</summary>
    public string MoveToUrl { get; private set; } = "";
    public string MoveToCertHash { get; private set; } = "";

    [TempData] public string? Message { get; set; }
    [TempData] public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    /// <summary>
    /// Kopiert die aktuelle Agent-MSI an einen abweichenden Zielpfad (Override). Ohne Benutzer/Passwort schreibt der
    /// Server mit eigener Identität; mit Zugangsdaten einmalig für diese Kopie (z. B. NETLOGON). Zugangsdaten werden nie gespeichert.
    /// </summary>
    public IActionResult OnPostCopy(string? targetPath, string? user, string? password)
    {
        targetPath = targetPath?.Trim() ?? "";
        _db.SetSetting(AgentDistribution.TargetSettingKey, targetPath);   // nur der Pfad wird gespeichert, nie die Zugangsdaten

        var (ok, msg) = _dist.CopyToTarget(targetPath, user, password);
        if (ok)
        {
            Message = msg;
            MessageIsError = false;
        }
        else
        {
            MessageIsError = true;
            Message = "Kopieren fehlgeschlagen: " + msg +
                      " Bitte manuell kopieren: Quelle „" + _dist.SourceMsiPath + "“ → Ziel „" + targetPath + "“.";
        }
        return RedirectToPage();
    }

    /// <summary>Serverumzug starten: Agenten künftig an den neuen Server verweisen (sie übernehmen ihn selbst).</summary>
    public IActionResult OnPostMigrate(string? newUrl, string? newCertHash)
    {
        newUrl = newUrl?.Trim() ?? "";
        newCertHash = (newCertHash ?? "").Replace(" ", "").Replace(":", "").Trim().ToUpperInvariant();

        if (newUrl.Length == 0 || newCertHash.Length == 0)
        {
            MessageIsError = true;
            Message = "Für den Serverumzug werden Adresse und Zertifikats-Fingerabdruck des neuen Servers benötigt.";
            return RedirectToPage();
        }

        _db.SetSetting(MigrationSettings.UrlKey, newUrl);
        _db.SetSetting(MigrationSettings.CertHashKey, newCertHash);
        _db.AddEvent("", "Serverumzug gestartet: Agenten werden auf " + newUrl + " umgezogen.");
        Message = "Serverumzug aktiv. Die Agenten stellen bei ihrer nächsten Meldung auf " + newUrl +
                  " um, sobald dieser erreichbar ist und sein Zertifikat passt.";
        MessageIsError = false;
        return RedirectToPage();
    }

    /// <summary>Serverumzug abbrechen: keine weiteren Agenten mehr umziehen.</summary>
    public IActionResult OnPostMigrateCancel()
    {
        _db.SetSetting(MigrationSettings.UrlKey, "");
        _db.SetSetting(MigrationSettings.CertHashKey, "");
        _db.AddEvent("", "Serverumzug beendet.");
        Message = "Serverumzug beendet. Noch nicht umgezogene Agenten bleiben auf diesem Server.";
        MessageIsError = false;
        return RedirectToPage();
    }

    private void Load()
    {
        var all = _db.GetAllStatus().Values.Where(s => s.LastContact != null).ToList();
        Reporting = all.Count;
        ReportingToday = all.Count(s => s.LastContact > DateTime.Now.AddDays(-1));
        AgentVersion = typeof(AgentModel).Assembly.GetName().Version?.ToString(3) ?? "";
        RecentEvents = _db.GetEvents(null, 100);
        TargetPath = _db.GetSetting(AgentDistribution.TargetSettingKey) ?? "";
        ShareError = _db.GetSetting(AgentShareService.ShareErrorSetting) is { Length: > 0 } err ? err : null;
        ShareUnc = $@"\\{new Uri(Identity.Url).Host}\{AgentDistribution.ShareName}\{AgentDistribution.FileName}";
        MoveToUrl = _db.GetSetting(MigrationSettings.UrlKey) ?? "";
        MoveToCertHash = _db.GetSetting(MigrationSettings.CertHashKey) ?? "";

        if (_msi.TemplateExists)
        {
            try { MsiPath = _msi.GetMsiPath(); }
            catch (Exception ex) { MsiError = ex.Message; }
        }
    }
}
