using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

public sealed class AgentModel : PageModel
{
    private const string TargetSetting = "AgentTargetPath";

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

    /// <summary>Gespeicherter Zielpfad für die automatische Verteilung (ohne Zugangsdaten).</summary>
    public string TargetPath { get; private set; } = "";

    [TempData] public string? Message { get; set; }
    [TempData] public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    /// <summary>
    /// Kopiert die aktuelle Agent-MSI an den Zielpfad. Ohne Benutzer/Passwort schreibt der Server mit eigener Identität
    /// (eigene Freigabe); mit Zugangsdaten einmalig für diese Kopie (z. B. NETLOGON). Zugangsdaten werden nie gespeichert.
    /// </summary>
    public IActionResult OnPostCopy(string? targetPath, string? user, string? password)
    {
        targetPath = targetPath?.Trim() ?? "";
        _db.SetSetting(TargetSetting, targetPath);   // nur der Pfad wird gespeichert, nie die Zugangsdaten

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

    private void Load()
    {
        var all = _db.GetAllStatus().Values.Where(s => s.LastContact != null).ToList();
        Reporting = all.Count;
        ReportingToday = all.Count(s => s.LastContact > DateTime.Now.AddDays(-1));
        AgentVersion = typeof(AgentModel).Assembly.GetName().Version?.ToString(3) ?? "";
        RecentEvents = _db.GetEvents(null, 100);
        TargetPath = _db.GetSetting(TargetSetting) ?? "";

        if (_msi.TemplateExists)
        {
            try { MsiPath = _msi.GetMsiPath(); }
            catch (Exception ex) { MsiError = ex.Message; }
        }
    }
}
