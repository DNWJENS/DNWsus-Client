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

                    // Verpasste Termine (Server war aus) lösen nur einen Lauf aus.
                    _db.CreateRun(schedule.GroupNumber, null, due, "Zeitplan", "Zeitplan: " + schedule.Description);
                    _db.SetScheduleFired(schedule.Id, now);
                    _logger.LogInformation("Zeitplan {Id} ({Description}) hat einen Lauf für Gruppe {Group} angelegt",
                        schedule.Id, schedule.Description, schedule.GroupNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Prüfen der Zeitpläne");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
