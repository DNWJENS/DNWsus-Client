using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

public sealed class AgentModel : PageModel
{
    private readonly AgentMsiService _msi;
    private readonly Database _db;

    public AgentModel(AgentMsiService msi, ServerIdentity identity, Database db)
    {
        _msi = msi;
        _db = db;
        Identity = identity;
    }

    public ServerIdentity Identity { get; }
    public bool TemplateExists => _msi.TemplateExists;
    public string AgentVersion { get; private set; } = "";
    public int Reporting { get; private set; }
    public int ReportingToday { get; private set; }
    public List<EventEntry> RecentEvents { get; private set; } = new();

    public void OnGet()
    {
        var all = _db.GetAllStatus().Values.Where(s => s.LastContact != null).ToList();
        Reporting = all.Count;
        ReportingToday = all.Count(s => s.LastContact > DateTime.Now.AddDays(-1));
        AgentVersion = typeof(AgentModel).Assembly.GetName().Version?.ToString(3) ?? "";
        RecentEvents = _db.GetEvents(null, 100);
    }
}
