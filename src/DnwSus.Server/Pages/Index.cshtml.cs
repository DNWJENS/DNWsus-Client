using System.Globalization;
using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DnwSus.Server.Pages;

/// <summary>Eine Zeile der Client-Tabelle: AD-Daten + letzter Stand vom Agenten + offener Lauf.</summary>
public sealed class ClientRow
{
    public ClientInfo Ad { get; init; } = new();
    public ClientStatus? Status { get; init; }
    public DateTime? PendingRunStart { get; init; }
}

public sealed class IndexModel : PageModel
{
    private readonly AdService _ad;
    private readonly Database _db;

    public IndexModel(AdService ad, Database db)
    {
        _ad = ad;
        _db = db;
    }

    public static readonly string[] Behaviour =
    {
        "",
        "Alle Updates installieren. Neustart nur mit Zustimmung des Benutzers: Popup „Jetzt neu starten“ oder „Zeitpunkt wählen“ (max. 24 Stunden), Erinnerung alle 30 Minuten.",
        "Alle Updates installieren. Der Benutzer wählt den Neustart: sofort, in 15, 60 oder 120 Minuten. Spätestens nach 2 Stunden startet der PC automatisch neu (5 Minuten vorher Hinweis).",
        "Alle Updates installieren. Ist ein Neustart nötig, startet der PC nach 5 Minuten automatisch neu – mit Hinweis und Knopf „Jetzt neu starten“."
    };

    public AdSnapshot Snapshot { get; private set; } = new();
    public string Tab { get; private set; } = "uebersicht";
    public int GroupNumber { get; private set; }
    public GroupInfo? CurrentGroup { get; private set; }
    public List<ClientRow> Rows { get; private set; } = new();
    public List<Run> Runs { get; private set; } = new();
    public List<Schedule> Schedules { get; private set; } = new();

    [TempData] public string? Message { get; set; }

    /// <summary>Aktuelle Agent-Version (= Server-Version), nur zur Information im Dashboard.</summary>
    public static string CurrentAgentVersion { get; } =
        typeof(IndexModel).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>
    /// Kleinste Agent-Version, die noch problemlos mit diesem Server zusammenarbeitet. Nur ältere Agenten werden als
    /// „inkompatibel" markiert – bewusst NICHT an die Server-/Dashboard-Version gekoppelt, damit ein reines Server-Update
    /// nicht alle Agenten fälschlich als „alt" flaggt. Nur erhöhen, wenn eine echte, nicht abwärtskompatible Änderung
    /// am Austauschformat (AgentContracts/Contracts) gemacht wird.
    /// </summary>
    public static readonly Version MinCompatibleAgentVersion = new(2, 0, 0);

    public bool IsAgentIncompatible(ClientStatus? st) =>
        st?.LastContact != null && Version.TryParse(st.AgentVersion, out var v) && v < MinCompatibleAgentVersion;

    public int Ungrouped => Snapshot.Clients.Count(c => c.Groups.Count == 0);
    public int Conflicts => Snapshot.Clients.Count(c => c.Groups.Count > 1);
    public int CountInGroup(int number) => Snapshot.Clients.Count(c => c.Groups.Contains(number));

    public void OnGet(string? tab, bool refresh = false)
    {
        Snapshot = _ad.GetSnapshot(refresh);
        Tab = tab?.ToLowerInvariant() ?? "uebersicht";
        var status = _db.GetAllStatus();

        IEnumerable<ClientInfo> clients;
        switch (Tab)
        {
            case "grp1":
            case "grp2":
            case "grp3":
                GroupNumber = Tab[3] - '0';
                CurrentGroup = Snapshot.Groups.FirstOrDefault(g => g.Number == GroupNumber);
                clients = Snapshot.Clients.Where(c => c.Groups.Contains(GroupNumber));
                Runs = _db.GetRuns(GroupNumber, activeOnly: false, limit: 20);
                Schedules = _db.GetSchedules(GroupNumber);
                break;
            case "ohne":
                clients = Snapshot.Clients.Where(c => c.Groups.Count == 0);
                break;
            default:
                Tab = "uebersicht";
                clients = Snapshot.Clients;
                break;
        }

        var list = clients.ToList();
        var pending = GroupNumber > 0
            ? _db.GetPendingRunStarts(GroupNumber, list.Where(c => c.Groups.Count == 1).Select(c => c.Name))
            : new Dictionary<string, DateTime>();

        Rows = list.Select(c => new ClientRow
        {
            Ad = c,
            Status = status.GetValueOrDefault(c.Name),
            PendingRunStart = pending.TryGetValue(c.Name, out var start) ? start : null
        }).ToList();
    }

    // ------------------------------------------------------------------ Aktionen

    public IActionResult OnPostInstall(int group, string[]? selected, string? startAt, bool whole)
    {
        if (group is < 1 or > 3) return BadRequest();
        DateTime start = ParseLocal(startAt) ?? DateTime.Now;
        string by = User.Identity?.Name ?? "";
        string when = start <= DateTime.Now ? "sofort" : "ab " + start.ToString("dd.MM.yyyy HH:mm");

        if (whole)
        {
            _db.CreateRun(group, null, start, by, "manuell");
            Message = $"Lauf für die ganze Gruppe angelegt ({when}). Die Clients holen ihn bei ihrer nächsten Meldung ab (spätestens nach 5 Minuten).";
        }
        else
        {
            var names = (selected ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (names.Count == 0)
            {
                Message = "Keine Clients ausgewählt.";
                return RedirectToPage(new { tab = "grp" + group });
            }
            foreach (var name in names) _db.CreateRun(group, name, start, by, "manuell");
            Message = $"Lauf für {names.Count} Client(s) angelegt ({when}).";
        }
        return RedirectToPage(new { tab = "grp" + group });
    }

    public IActionResult OnPostScan(string tab, string[]? selected)
    {
        var names = (selected ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        _db.RequestScan(names);
        Message = names.Count == 0
            ? "Keine Clients ausgewählt."
            : $"Neuer Scan für {names.Count} Client(s) angefordert (bei der nächsten Meldung, spätestens nach 5 Minuten).";
        return RedirectToPage(new { tab });
    }

    public IActionResult OnPostCancelRun(int group, long id)
    {
        _db.CancelRun(id);
        Message = $"Lauf {id} beendet. Clients, die ihn noch nicht abgeholt haben, installieren nicht mehr.";
        return RedirectToPage(new { tab = "grp" + group });
    }

    /// <summary>
    /// Manuelle Soforthilfe aus dem Bulk-Menü: für die ausgewählten Clients (mit Agent, gruppenunabhängig) einen Lauf
    /// mit Updatestart und optionaler Reboot-Zeit anlegen. Übersteuert für diesen Lauf das Gruppen-Neustart-Verhalten.
    /// </summary>
    public IActionResult OnPostBulk(string tab, string[]? selected, string? startAt, string? rebootAt)
    {
        var names = (selected ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0)
        {
            Message = "Keine Clients ausgewählt.";
            return RedirectToPage(new { tab });
        }

        DateTime start = ParseLocal(startAt) ?? DateTime.Now;
        DateTime? reboot = ParseLocal(rebootAt);
        string by = User.Identity?.Name ?? "";
        var snapshot = _ad.GetSnapshot();

        int created = 0, skipped = 0;
        foreach (var name in names)
        {
            // Nur Clients mit Agent; sonst lässt sich nichts installieren.
            if (_db.GetStatus(name)?.LastContact == null) { skipped++; continue; }
            var client = snapshot.Clients.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            int group = client is { Groups.Count: 1 } ? client.Groups[0] : 0;
            _db.CreateRun(group, name, start, by, "Soforthilfe", isManual: true, rebootAt: reboot);
            created++;
        }

        string when = start <= DateTime.Now ? "sofort" : "ab " + start.ToString("dd.MM.yyyy HH:mm");
        string reb = reboot.HasValue ? $", Neustart falls nötig um {reboot.Value:dd.MM.yyyy HH:mm}" : ", kein erzwungener Neustart";
        Message = $"Soforthilfe für {created} Client(s) angelegt (Updatestart {when}{reb})." +
                  (skipped > 0 ? $" {skipped} ohne Agent übersprungen." : "");
        return RedirectToPage(new { tab });
    }

    /// <summary>Braucht der Client (laut Scan oder Windows) einen Neustart?</summary>
    public static bool RebootNeeded(ClientStatus? st) =>
        st != null && (st.RebootPendingSince != null || st.PendingUpdates.Any(u => u.RebootRequired));

    public IActionResult OnPostAddSchedule(int group, string kind, int[]? days, int offset, string? time)
    {
        if (group is < 1 or > 3) return BadRequest();
        if (!TimeSpan.TryParseExact(time ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out var at))
        {
            Message = "Bitte eine gültige Uhrzeit angeben.";
            return RedirectToPage(new { tab = "grp" + group });
        }

        var schedule = new Schedule { GroupNumber = group, Time = at };
        if (kind == "patchday")
        {
            schedule.Kind = ScheduleKind.Patchday;
            schedule.OffsetDays = Math.Clamp(offset, 0, 27);
        }
        else
        {
            schedule.Kind = ScheduleKind.Woechentlich;
            schedule.Days = (days ?? Array.Empty<int>()).Where(d => d is >= 0 and <= 6).Distinct().Select(d => (DayOfWeek)d).ToList();
            if (schedule.Days.Count == 0)
            {
                Message = "Bitte mindestens einen Wochentag auswählen.";
                return RedirectToPage(new { tab = "grp" + group });
            }
        }

        _db.AddSchedule(schedule);
        Message = "Zeitplan angelegt: " + schedule.Description + ". Nächster Termin: " +
                  schedule.NextAfter(DateTime.Now).ToString("dd.MM.yyyy HH:mm") + ".";
        return RedirectToPage(new { tab = "grp" + group });
    }

    public IActionResult OnPostDeleteSchedule(int group, long id)
    {
        _db.DeleteSchedule(id);
        Message = "Zeitplan gelöscht.";
        return RedirectToPage(new { tab = "grp" + group });
    }

    // ------------------------------------------------------------------ Anzeige

    public string GroupLabel(ClientInfo client)
    {
        if (client.Groups.Count == 0) return "keine";
        return string.Join(", ", client.Groups.Select(n => Snapshot.Groups.FirstOrDefault(g => g.Number == n)?.Name ?? n.ToString()));
    }

    public static string StateText(ClientStatus? status)
    {
        if (status?.LastContact == null) return "kein Agent";
        if (status.RebootPendingSince.HasValue)
            return "Neustart ausstehend seit " + FormatAge(status.RebootPendingSince, withTime: true);
        return string.IsNullOrEmpty(status.State) ? "—" : status.State;
    }

    /// <summary>Letzter Kontakt älter als 1 Tag → Warnfarbe.</summary>
    public static bool ContactIsOld(ClientStatus? status) =>
        status?.LastContact != null && status.LastContact < DateTime.Now.AddDays(-1);

    public static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm") : "—";

    public static string FormatAge(DateTime? value, bool withTime = false)
    {
        if (!value.HasValue) return "nie";
        var span = DateTime.Now - value.Value;
        if (withTime && span.TotalHours < 24)
            return span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} min" : $"{(int)span.TotalHours} h";
        if (span.TotalMinutes < 60) return $"vor {Math.Max(0, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"vor {(int)span.TotalHours} h";
        var days = (int)span.TotalDays;
        return withTime ? $"{days} Tag(en)" : days == 1 ? "gestern" : $"vor {days} Tagen";
    }

    private static DateTime? ParseLocal(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
