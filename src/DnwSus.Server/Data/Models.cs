namespace DnwSus.Server.Data;

/// <summary>Letzter gemeldeter Stand eines Clients (aus den Meldungen des Agenten).</summary>
public sealed class ClientStatus
{
    public string Name { get; set; } = "";
    public string DnsName { get; set; } = "";
    public string IpAddresses { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public DateTime? BootTime { get; set; }
    public string CurrentUser { get; set; } = "";
    public string LastUser { get; set; } = "";
    public DateTime? LastContact { get; set; }
    public string State { get; set; } = "";
    public DateTime? RebootPendingSince { get; set; }
    public DateTime? LastScan { get; set; }
    public int PendingCount { get; set; }
    public List<PendingUpdate> PendingUpdates { get; set; } = new();
    public string LastError { get; set; } = "";
    public bool ScanRequested { get; set; }

    public string UptimeText
    {
        get
        {
            if (BootTime == null) return "—";
            var span = DateTime.Now - BootTime.Value;
            return span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h" : $"{span.Hours} h {span.Minutes} min";
        }
    }

    /// <summary>Benutzer für die Anzeige: angemeldeter Benutzer, sonst der zuletzt angemeldete.</summary>
    public string UserText => !string.IsNullOrEmpty(CurrentUser) ? CurrentUser : LastUser;
}

public sealed class PendingUpdate
{
    public string KB { get; set; } = "";
    public string Title { get; set; } = "";
    public double SizeMB { get; set; }
    public bool RebootRequired { get; set; }
    public string Category { get; set; } = "";
}

/// <summary>Ein Installationslauf: für eine ganze Gruppe oder einen einzelnen Client.</summary>
public sealed class Run
{
    public long Id { get; set; }
    public int GroupNumber { get; set; }
    public string? Client { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Cancelled { get; set; }
    public int DoneCount { get; set; }
}

public enum ScheduleKind { Woechentlich, Patchday }

/// <summary>Fester Zeitplan einer Gruppe, der automatisch Läufe anlegt.</summary>
public sealed class Schedule
{
    public long Id { get; set; }
    public int GroupNumber { get; set; }
    public ScheduleKind Kind { get; set; }

    /// <summary>Wöchentlich: Wochentage (0 = Sonntag … 6 = Samstag).</summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary>Patchday: Tage nach dem 2. Dienstag des Monats.</summary>
    public int OffsetDays { get; set; }

    public TimeSpan Time { get; set; }
    public DateTime LastFired { get; set; }

    private static readonly string[] DayNames = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

    public string Description => Kind == ScheduleKind.Woechentlich
        ? $"Wöchentlich {string.Join(", ", Days.OrderBy(d => ((int)d + 6) % 7).Select(d => DayNames[(int)d]))} um {Time:hh\\:mm}"
        : $"Patchday (2. Dienstag) + {OffsetDays} Tag(e) um {Time:hh\\:mm}";

    /// <summary>Nächster Auslösezeitpunkt nach <paramref name="after"/>.</summary>
    public DateTime NextAfter(DateTime after)
    {
        if (Kind == ScheduleKind.Woechentlich)
        {
            if (Days.Count == 0) return DateTime.MaxValue;
            for (int i = 0; i <= 7; i++)
            {
                var candidate = after.Date.AddDays(i) + Time;
                if (candidate > after && Days.Contains(candidate.DayOfWeek)) return candidate;
            }
            return DateTime.MaxValue;
        }

        for (int m = -1; m <= 2; m++)
        {
            var month = new DateTime(after.Year, after.Month, 1).AddMonths(m);
            var candidate = SecondTuesday(month).AddDays(OffsetDays) + Time;
            if (candidate > after) return candidate;
        }
        return DateTime.MaxValue;
    }

    private static DateTime SecondTuesday(DateTime firstOfMonth)
    {
        int offset = ((int)DayOfWeek.Tuesday - (int)firstOfMonth.DayOfWeek + 7) % 7;
        return firstOfMonth.AddDays(offset + 7);
    }
}

public sealed class EventEntry
{
    public DateTime Time { get; set; }
    public string Client { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsError { get; set; }
}
