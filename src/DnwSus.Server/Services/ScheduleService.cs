using DnwSus.Server.Data;

namespace DnwSus.Server.Services;

/// <summary>Prüft alle 30 Sekunden die Zeitpläne der Gruppen und legt fällige Läufe an.</summary>
public sealed class ScheduleService : BackgroundService
{
    private readonly Database _db;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(Database db, ILogger<ScheduleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var now = DateTime.Now;
                foreach (var schedule in _db.GetSchedules())
                {
                    var due = schedule.NextAfter(schedule.LastFired);
                    if (due > now) continue;

                    // Welche Klassen installiert dieser Zeitplan? Eine feste Klasse, sonst alle „Nach Zeitplan"-Klassen der Gruppe.
                    string classes = ClassesFor(schedule);
                    _db.SetScheduleFired(schedule.Id, now);
                    if (classes.Length == 0) continue; // nichts auf „Nach Zeitplan" → kein Lauf

                    // Verpasste Termine (Server war aus) lösen nur einen Lauf aus.
                    _db.CreateRun(schedule.GroupNumber, null, due, "Zeitplan", "Zeitplan: " + schedule.Description, classes: classes);
                    _logger.LogInformation("Zeitplan {Id} ({Description}) hat einen Lauf für Gruppe {Group} angelegt (Klassen: {Classes})",
                        schedule.Id, schedule.Description, schedule.GroupNumber, classes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Prüfen der Zeitpläne");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Klassen, die dieser Zeitplan installiert: die feste Klasse (falls gesetzt), sonst alle „Nach Zeitplan"-Klassen der Gruppe.</summary>
    private string ClassesFor(Schedule schedule)
    {
        if (!string.IsNullOrEmpty(schedule.Class)) return schedule.Class!;
        var scheduled = _db.GetPolicies(schedule.GroupNumber)
            .Where(p => p.Value.Install == UpdatePolicy.Schedule)
            .Select(p => p.Key);
        return string.Join(",", scheduled);
    }
}
