using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

public sealed class IndexModel : PageModel
{
    private readonly AdService _ad;

    public IndexModel(AdService ad) => _ad = ad;

    public AdSnapshot Snapshot { get; private set; } = new();

    /// <summary>uebersicht, grp1, grp2, grp3, ohne</summary>
    public string Tab { get; private set; } = "uebersicht";

    public List<ClientInfo> Rows { get; private set; } = new();

    public GroupInfo? CurrentGroup { get; private set; }

    public int Ungrouped => Snapshot.Clients.Count(c => c.Groups.Count == 0);
    public int Conflicts => Snapshot.Clients.Count(c => c.Groups.Count > 1);
    public int CountInGroup(int number) => Snapshot.Clients.Count(c => c.Groups.Contains(number));

    public void OnGet(string? tab, bool refresh = false)
    {
        Snapshot = _ad.GetSnapshot(refresh);
        Tab = tab?.ToLowerInvariant() ?? "uebersicht";

        switch (Tab)
        {
            case "grp1":
            case "grp2":
            case "grp3":
                int number = Tab[3] - '0';
                CurrentGroup = Snapshot.Groups.FirstOrDefault(g => g.Number == number);
                Rows = Snapshot.Clients.Where(c => c.Groups.Contains(number)).ToList();
                break;
            case "ohne":
                Rows = Snapshot.Clients.Where(c => c.Groups.Count == 0).ToList();
                break;
            default:
                Tab = "uebersicht";
                Rows = Snapshot.Clients;
                break;
        }
    }

    public string GroupLabel(ClientInfo client)
    {
        if (client.Groups.Count == 0) return "keine";
        return string.Join(", ", client.Groups.Select(n => Snapshot.Groups.FirstOrDefault(g => g.Number == n)?.Name ?? n.ToString()));
    }

    public static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm") : "—";

    public static string FormatAge(DateTime? value)
    {
        if (!value.HasValue) return "nie";
        var days = (int)(DateTime.Now - value.Value).TotalDays;
        return days switch { 0 => "heute", 1 => "gestern", _ => $"vor {days} Tagen" };
    }
}
