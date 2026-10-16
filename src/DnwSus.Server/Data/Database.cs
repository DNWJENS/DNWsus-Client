using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using DnwSus.Server.Api;
using Microsoft.Data.Sqlite;

namespace DnwSus.Server.Data;

/// <summary>
/// Datenhaltung in einer SQLite-Datei unter C:\ProgramData\DNWsus\Server.
/// Der Ordner ist nur für SYSTEM und Administratoren lesbar (enthält Benutzernamen und IPs).
/// </summary>
public sealed class Database
{
    private readonly object _lock = new();
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private readonly ILogger<Database> _logger;

    public string DataDirectory { get; }

    public Database(ILogger<Database> logger)
    {
        _logger = logger;
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DNWsus", "Server");
        CreateProtectedDirectory(DataDirectory);

        _connection = new SqliteConnection("Data Source=" + Path.Combine(DataDirectory, "dnwsus.db"));
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute(@"
CREATE TABLE IF NOT EXISTS clients (
    name TEXT PRIMARY KEY COLLATE NOCASE,
    dns TEXT, ips TEXT, os TEXT, agent_version TEXT, boot_time TEXT,
    current_user TEXT, last_user TEXT, last_contact TEXT, state TEXT,
    reboot_pending_since TEXT, last_scan TEXT, pending_count INTEGER DEFAULT 0,
    pending_json TEXT, last_error TEXT, scan_requested INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_no INTEGER NOT NULL, client TEXT COLLATE NOCASE,
    start_at TEXT NOT NULL, created_at TEXT NOT NULL, created_by TEXT, source TEXT,
    cancelled INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS run_done (
    run_id INTEGER NOT NULL, client TEXT NOT NULL COLLATE NOCASE,
    finished_at TEXT, success INTEGER, message TEXT,
    PRIMARY KEY (run_id, client));
CREATE TABLE IF NOT EXISTS schedules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_no INTEGER NOT NULL, kind TEXT NOT NULL, days TEXT, offset_days INTEGER,
    time TEXT NOT NULL, last_fired TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    time TEXT NOT NULL, client TEXT COLLATE NOCASE, text TEXT, is_error INTEGER DEFAULT 0);
CREATE INDEX IF NOT EXISTS ix_events_client ON events(client, id);");
    }

    // ------------------------------------------------------------------ Clients

    public void SaveReport(string name, AgentReport report)
    {
        var now = DateTime.Now;
        lock (_lock)
        {
            using var tx = _connection.BeginTransaction();
            _transaction = tx;
            try
            {
                SaveReportLocked(name, report, now);
                tx.Commit();
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    private void SaveReportLocked(string name, AgentReport report, DateTime now)
    {
        Execute(@"
INSERT INTO clients (name) VALUES ($name) ON CONFLICT(name) DO NOTHING;
UPDATE clients SET dns=$dns, ips=$ips, os=$os, agent_version=$ver, boot_time=$boot,
current_user=$cur, last_user=$last, last_contact=$now, state=$state,
reboot_pending_since=$reboot, last_error=$err
WHERE name=$name;",
            ("$name", name), ("$dns", report.DnsName), ("$ips", report.IpAddresses), ("$os", report.OsVersion),
            ("$ver", report.AgentVersion), ("$boot", NormalizeDate(report.BootTime)),
            ("$cur", report.CurrentUser), ("$last", report.LastUser), ("$now", Format(now)),
            ("$state", report.State), ("$reboot", NormalizeDate(report.RebootPendingSince)),
            ("$err", report.LastError));

        if (report.Scan != null)
        {
            var updates = report.Scan.Updates.Select(u => new PendingUpdate
            {
                KB = u.KB, Title = u.Title, SizeMB = u.SizeMB, RebootRequired = u.RebootRequired, Category = u.Category
            }).ToList();
            Execute("UPDATE clients SET last_scan=$scan, pending_count=$count, pending_json=$json, scan_requested=0 WHERE name=$name;",
                ("$name", name), ("$scan", NormalizeDate(report.Scan.ScannedAt) ?? Format(now)),
                ("$count", updates.Count), ("$json", JsonSerializer.Serialize(updates)));
        }

        if (report.Install != null)
        {
            var install = report.Install;
            foreach (var runId in install.RunIds)
            {
                Execute(@"INSERT OR REPLACE INTO run_done (run_id, client, finished_at, success, message)
                          VALUES ($run, $name, $now, $ok, $msg);",
                    ("$run", runId), ("$name", name), ("$now", Format(now)),
                    ("$ok", install.Success ? 1 : 0), ("$msg", install.Message));
            }

            string summary = install.Success
                ? $"Installation abgeschlossen: {install.InstalledCount} Update(s) installiert" +
                  (install.FailedCount > 0 ? $", {install.FailedCount} fehlgeschlagen" : "") +
                  (install.RebootRequired ? ", Neustart erforderlich" : "")
                : "Installation fehlgeschlagen: " + install.Message;
            AddEventLocked(name, summary, !install.Success || install.FailedCount > 0);
            foreach (var item in install.Items)
                AddEventLocked(name, $"  {(item.Success ? "✓" : "✗")} {(string.IsNullOrEmpty(item.KB) ? "" : "KB" + item.KB + " ")}{item.Title}" +
                                     (item.Success ? "" : " – " + item.Result), !item.Success);
        }
    }

    public Dictionary<string, ClientStatus> GetAllStatus()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, ClientStatus>(StringComparer.OrdinalIgnoreCase);
            using var cmd = Command("SELECT * FROM clients;");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var status = ReadStatus(reader);
                result[status.Name] = status;
            }
            return result;
        }
    }

    public ClientStatus? GetStatus(string name)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT * FROM clients WHERE name=$name;", ("$name", name));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadStatus(reader) : null;
        }
    }

    public bool IsScanRequested(string name)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT scan_requested FROM clients WHERE name=$name;", ("$name", name));
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 1;
        }
    }

    public void RequestScan(IEnumerable<string> names)
    {
        lock (_lock)
        {
            foreach (var name in names)
                Execute("INSERT INTO clients (name, scan_requested) VALUES ($name, 1) ON CONFLICT(name) DO UPDATE SET scan_requested=1;",
                    ("$name", name));
        }
    }

    // ------------------------------------------------------------------ Läufe

    public long CreateRun(int group, string? client, DateTime startAt, string createdBy, string source)
    {
        lock (_lock)
        {
            using var cmd = Command(@"INSERT INTO runs (group_no, client, start_at, created_at, created_by, source)
                                      VALUES ($g, $c, $start, $now, $by, $src); SELECT last_insert_rowid();",
                ("$g", group), ("$c", client), ("$start", Format(startAt)), ("$now", Format(DateTime.Now)),
                ("$by", createdBy), ("$src", source));
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    public void CancelRun(long id)
    {
        lock (_lock) Execute("UPDATE runs SET cancelled=1 WHERE id=$id;", ("$id", id));
    }

    /// <summary>Läufe, die dieser Client jetzt installieren soll: gestartet, nicht abgebrochen, von ihm noch nicht erledigt.</summary>
    public List<long> GetOpenRunIds(string client, int group, DateTime now)
    {
        lock (_lock)
        {
            using var cmd = Command(@"
SELECT r.id FROM runs r
WHERE r.cancelled=0 AND r.group_no=$g AND r.start_at <= $now
  AND (r.client IS NULL OR r.client=$c)
  AND NOT EXISTS (SELECT 1 FROM run_done d WHERE d.run_id=r.id AND d.client=$c)
ORDER BY r.id;", ("$g", group), ("$now", Format(now)), ("$c", client));
            using var reader = cmd.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids;
        }
    }

    /// <summary>Clients mit offenem (oder geplantem) Lauf in dieser Gruppe, mit Startzeit des ältesten offenen Laufs.</summary>
    public Dictionary<string, DateTime> GetPendingRunStarts(int group, IEnumerable<string> members)
    {
        var runs = GetRuns(group, activeOnly: true);
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            foreach (var member in members)
            {
                foreach (var run in runs.OrderBy(r => r.StartAt))
                {
                    if (run.Client != null && !run.Client.Equals(member, StringComparison.OrdinalIgnoreCase)) continue;
                    using var cmd = Command("SELECT 1 FROM run_done WHERE run_id=$r AND client=$c;", ("$r", run.Id), ("$c", member));
                    if (cmd.ExecuteScalar() == null)
                    {
                        result[member] = run.StartAt;
                        break;
                    }
                }
            }
        }
        return result;
    }

    public List<Run> GetRuns(int group, bool activeOnly, int limit = 50)
    {
        lock (_lock)
        {
            using var cmd = Command(@"
SELECT r.*, (SELECT COUNT(*) FROM run_done d WHERE d.run_id=r.id) AS done
FROM runs r WHERE r.group_no=$g" + (activeOnly ? " AND r.cancelled=0" : "") + @"
ORDER BY r.id DESC LIMIT $limit;", ("$g", group), ("$limit", limit));
            using var reader = cmd.ExecuteReader();
            var runs = new List<Run>();
            while (reader.Read())
            {
                runs.Add(new Run
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    GroupNumber = reader.GetInt32(reader.GetOrdinal("group_no")),
                    Client = reader.IsDBNull(reader.GetOrdinal("client")) ? null : reader.GetString(reader.GetOrdinal("client")),
                    StartAt = ParseDate(reader["start_at"]) ?? DateTime.MinValue,
                    CreatedAt = ParseDate(reader["created_at"]) ?? DateTime.MinValue,
                    CreatedBy = reader["created_by"] as string ?? "",
                    Source = reader["source"] as string ?? "",
                    Cancelled = Convert.ToInt64(reader["cancelled"]) == 1,
                    DoneCount = Convert.ToInt32(reader["done"])
                });
            }
            return runs;
        }
    }

    // ------------------------------------------------------------------ Zeitpläne

    public List<Schedule> GetSchedules(int? group = null)
    {
        lock (_lock)
        {
            using var cmd = group.HasValue
                ? Command("SELECT * FROM schedules WHERE group_no=$g ORDER BY id;", ("$g", group.Value))
                : Command("SELECT * FROM schedules ORDER BY id;");
            using var reader = cmd.ExecuteReader();
            var list = new List<Schedule>();
            while (reader.Read())
            {
                list.Add(new Schedule
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    GroupNumber = reader.GetInt32(reader.GetOrdinal("group_no")),
                    Kind = Enum.Parse<ScheduleKind>(reader.GetString(reader.GetOrdinal("kind"))),
                    Days = (reader["days"] as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => (DayOfWeek)int.Parse(d, CultureInfo.InvariantCulture)).ToList(),
                    OffsetDays = reader.IsDBNull(reader.GetOrdinal("offset_days")) ? 0 : reader.GetInt32(reader.GetOrdinal("offset_days")),
                    Time = TimeSpan.Parse(reader.GetString(reader.GetOrdinal("time")), CultureInfo.InvariantCulture),
                    LastFired = ParseDate(reader["last_fired"]) ?? DateTime.Now
                });
            }
            return list;
        }
    }

    public void AddSchedule(Schedule schedule)
    {
        lock (_lock)
        {
            Execute(@"INSERT INTO schedules (group_no, kind, days, offset_days, time, last_fired)
                      VALUES ($g, $kind, $days, $offset, $time, $fired);",
                ("$g", schedule.GroupNumber), ("$kind", schedule.Kind.ToString()),
                ("$days", string.Join(",", schedule.Days.Select(d => ((int)d).ToString(CultureInfo.InvariantCulture)))),
                ("$offset", schedule.OffsetDays), ("$time", schedule.Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture)),
                ("$fired", Format(DateTime.Now)));
        }
    }

    public void DeleteSchedule(long id)
    {
        lock (_lock) Execute("DELETE FROM schedules WHERE id=$id;", ("$id", id));
    }

    public void SetScheduleFired(long id, DateTime firedAt)
    {
        lock (_lock) Execute("UPDATE schedules SET last_fired=$t WHERE id=$id;", ("$id", id), ("$t", Format(firedAt)));
    }

    // ------------------------------------------------------------------ Ereignisse

    public void AddEvent(string client, string text, bool isError = false)
    {
        lock (_lock) AddEventLocked(client, text, isError);
    }

    public List<EventEntry> GetEvents(string? client, int limit = 200)
    {
        lock (_lock)
        {
            using var cmd = client == null
                ? Command("SELECT * FROM events ORDER BY id DESC LIMIT $l;", ("$l", limit))
                : Command("SELECT * FROM events WHERE client=$c ORDER BY id DESC LIMIT $l;", ("$c", client), ("$l", limit));
            using var reader = cmd.ExecuteReader();
            var list = new List<EventEntry>();
            while (reader.Read())
            {
                list.Add(new EventEntry
                {
                    Time = ParseDate(reader["time"]) ?? DateTime.MinValue,
                    Client = reader["client"] as string ?? "",
                    Text = reader["text"] as string ?? "",
                    IsError = Convert.ToInt64(reader["is_error"]) == 1
                });
            }
            return list;
        }
    }

    private void AddEventLocked(string client, string text, bool isError) =>
        Execute("INSERT INTO events (time, client, text, is_error) VALUES ($t, $c, $x, $e);",
            ("$t", Format(DateTime.Now)), ("$c", client), ("$x", text), ("$e", isError ? 1 : 0));

    // ------------------------------------------------------------------ Hilfsfunktionen

    private ClientStatus ReadStatus(SqliteDataReader reader)
    {
        var status = new ClientStatus
        {
            Name = reader["name"] as string ?? "",
            DnsName = reader["dns"] as string ?? "",
            IpAddresses = reader["ips"] as string ?? "",
            OsVersion = reader["os"] as string ?? "",
            AgentVersion = reader["agent_version"] as string ?? "",
            BootTime = ParseDate(reader["boot_time"]),
            CurrentUser = reader["current_user"] as string ?? "",
            LastUser = reader["last_user"] as string ?? "",
            LastContact = ParseDate(reader["last_contact"]),
            State = reader["state"] as string ?? "",
            RebootPendingSince = ParseDate(reader["reboot_pending_since"]),
            LastScan = ParseDate(reader["last_scan"]),
            PendingCount = reader["pending_count"] is long count ? (int)count : 0,
            LastError = reader["last_error"] as string ?? "",
            ScanRequested = reader["scan_requested"] is long sr && sr == 1
        };
        if (reader["pending_json"] is string json && json.Length > 0)
        {
            try { status.PendingUpdates = JsonSerializer.Deserialize<List<PendingUpdate>>(json) ?? new(); }
            catch (JsonException ex) { _logger.LogWarning(ex, "Update-Liste von {Client} nicht lesbar", status.Name); }
        }
        return status;
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _transaction;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        cmd.ExecuteNonQuery();
    }

    private static string Format(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? Format(parsed) : null;
    }

    private static DateTime? ParseDate(object? value) =>
        value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>Legt den Datenordner an und beschränkt den Zugriff auf SYSTEM und Administratoren.</summary>
    private static void CreateProtectedDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) dir.Create();

        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            {
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            dir.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            // Ohne Admin-Rechte (nur Entwicklung) bleibt es bei den geerbten Rechten.
        }
    }
}
