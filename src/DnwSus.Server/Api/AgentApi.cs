using System.Security.Principal;
using DnwSus.Server.Data;
using DnwSus.Server.Services;

namespace DnwSus.Server.Api;

/// <summary>
/// Endpunkte, die von den Clients aus erreichbar sind. Der Agent meldet sich per Kerberos mit dem
/// Computerkonto an (DOMÄNE\NAME$); er darf nur Daten für genau diesen Computer melden und abholen.
/// </summary>
public static class AgentApi
{
    public static void MapAgentApi(this WebApplication app)
    {
        app.MapPost("/api/agent/report", Report).RequireAuthorization();

        // MSI-Download: für jedes angemeldete Domänenkonto (Admins auf dem Server, Benutzer bei manueller Installation)
        app.MapGet("/download/DnwSus-Agent.msi", (AgentMsiService msi) =>
            Results.File(msi.GetMsiPath(), "application/x-msi", "DnwSus-Agent.msi")).RequireAuthorization();
    }

    private static IResult Report(HttpContext context, AgentReport report, Database db, AdService ad, ILogger<Database> logger)
    {
        string? account = GetComputerAccount(context);
        if (account == null)
            return Results.Text("Nur Computerkonten dürfen sich als Agent melden.", statusCode: StatusCodes.Status403Forbidden);

        if (!account.Equals(report.ComputerName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Agent-Meldung abgewiesen: Konto {Account} meldet sich als {Name}", account, report.ComputerName);
            return Results.Text("Computername passt nicht zum angemeldeten Computerkonto.", statusCode: StatusCodes.Status403Forbidden);
        }

        string name = account.ToUpperInvariant();
        db.SaveReport(name, report);

        var now = DateTime.Now;
        var client = ad.GetSnapshot().Clients.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        int group = client == null ? 0 : client.Groups.Count switch { 0 => 0, 1 => client.Groups[0], _ => -1 };

        var response = new AgentResponse
        {
            Group = group,
            ScanRequested = db.IsScanRequested(name),
            ServerTime = now.ToString("yyyy-MM-ddTHH:mm:ss")
        };

        // Gruppen-Läufe gelten nur mit gültiger Gruppe; manuelle Soforthilfe-Läufe gelten unabhängig davon.
        var open = db.GetOpenRuns(name, group > 0 ? group : 0, now);
        response.InstallRuns = open.Select(r => new InstallRun
        {
            Id = r.Id,
            Manual = r.IsManual,
            RebootAt = r.RebootAt.HasValue ? r.RebootAt.Value.ToString("yyyy-MM-ddTHH:mm:ss") : ""
        }).ToList();
        response.InstallRunIds = open.Select(r => r.Id).ToList();

        return Results.Json(response);
    }

    /// <summary>"DOMÄNE\PC01$" → "PC01"; null, wenn es kein Computerkonto ist.</summary>
    private static string? GetComputerAccount(HttpContext context)
    {
        if (context.User.Identity is not WindowsIdentity { IsAuthenticated: true } identity) return null;
        string name = identity.Name;
        if (!name.EndsWith('$')) return null;
        int slash = name.IndexOf('\\');
        return name.Substring(slash + 1, name.Length - slash - 2);
    }
}
