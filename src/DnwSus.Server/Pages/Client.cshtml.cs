using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

public sealed class ClientModel : PageModel
{
    private readonly AdService _ad;
    private readonly Database _db;

    public ClientModel(AdService ad, Database db)
    {
        _ad = ad;
        _db = db;
    }

    public string Name { get; private set; } = "";
    public ClientInfo? Ad { get; private set; }
    public ClientStatus? Status { get; private set; }
    public string GroupText { get; private set; } = "";
    public List<EventEntry> Events { get; private set; } = new();

    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return RedirectToPage("Index");
        Name = name.ToUpperInvariant();

        var snapshot = _ad.GetSnapshot();
        Ad = snapshot.Clients.FirstOrDefault(c => c.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));
        Status = _db.GetStatus(Name);
        Events = _db.GetEvents(Name, 300);

        GroupText = Ad == null
            ? "nicht als aktiver Client im AD gefunden"
            : Ad.Groups.Count == 0 ? "keine Update-Gruppe"
            : string.Join(", ", Ad.Groups.Select(n => snapshot.Groups.First(g => g.Number == n).Name)) +
              (Ad.Groups.Count > 1 ? " (Konflikt – wird nicht aktualisiert)" : "");
        return Page();
    }

    public IActionResult OnPostScan(string name)
    {
        _db.RequestScan(new[] { name });
        Message = "Neuer Scan angefordert (bei der nächsten Meldung, spätestens nach 5 Minuten).";
        return RedirectToPage(new { name });
    }

    public IActionResult OnPostInstall(string name)
    {
        var client = _ad.GetSnapshot().Clients.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (client is not { Groups.Count: 1 })
        {
            Message = "Installation nur für Clients in genau einer Update-Gruppe möglich.";
            return RedirectToPage(new { name });
        }
        _db.CreateRun(client.Groups[0], client.Name, DateTime.Now, User.Identity?.Name ?? "", "manuell");
        Message = "Lauf angelegt. Der Client installiert bei seiner nächsten Meldung (spätestens nach 5 Minuten).";
        return RedirectToPage(new { name });
    }
}
