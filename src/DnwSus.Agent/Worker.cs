using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DnwSus.Agent
{
    /// <summary>
    /// Kern des Agenten. Eine Schleife läuft alle 30 Sekunden:
    ///  - alle 5 Minuten Meldung an den Server (Status, neue Scan-/Installationsergebnisse), Antwort = Aufträge
    ///  - Neustart-Ablauf je nach Gruppe (Hinweise anzeigen, Neustart auslösen)
    /// Scan und Installation laufen in einem eigenen Thread, weil sie viele Minuten dauern können.
    /// </summary>
    internal sealed class Worker
    {
        // Exit-Codes der Hinweis-exe (siehe src/DnwSus.Hinweis/Program.cs)
        private const int HintRebootNow = 1;
        private const int HintDelayBase = 1000;

        private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(4);
        private static readonly TimeSpan PromptInterval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan CountdownLength = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Group2Deadline = TimeSpan.FromHours(2);

        private readonly object _sync = new object();
        private readonly ServerClient _server = new ServerClient();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private AgentState _state;
        private Thread _loop;
        private Thread _work;
        private volatile bool _stop;
        private DateTime _nextReport;
        private string _activity = "";
        private string _workError = "";
        private bool _hintRunning;
        private int _failedInstallAttempts;

        public void Start()
        {
            AgentPaths.EnsureDataDirectory();
            _state = AgentState.Load();
            Log.Info("Agent " + SystemInfo.AgentVersion + " gestartet, Server " + AgentPaths.ServerUrl);

            // Neustart ist inzwischen erfolgt → Ablauf beenden
            var pending = AgentState.Parse(_state.RebootPendingSince);
            if (pending.HasValue && SystemInfo.BootTime > pending.Value)
            {
                Log.Info("Neustart seit " + _state.RebootPendingSince + " ist erfolgt.");
                _state.ClearReboot();
                _state.Save();
            }

            _nextReport = DateTime.Now.AddSeconds(30); // Netzwerk nach dem Systemstart abwarten
            _loop = new Thread(Loop) { IsBackground = true, Name = "Agent-Schleife" };
            _loop.Start();
        }

        public void Stop()
        {
            _stop = true;
            _wake.Set();
            _loop?.Join(TimeSpan.FromSeconds(10));
            Log.Info("Agent beendet.");
        }

        private void Loop()
        {
            while (!_stop)
            {
                try
                {
                    if (DateTime.Now >= _nextReport) ReportCycle();
                    RebootTick();
                }
                catch (Exception ex)
                {
                    Log.Error("Fehler in der Agent-Schleife", ex);
                }
                _wake.WaitOne(TimeSpan.FromSeconds(30));
            }
        }

        // ------------------------------------------------------------------ Meldung an den Server

        private void ReportCycle()
        {
            AgentReport report;
            InstallReport sentInstall;
            bool sentScan;
            lock (_sync)
            {
                UpdateObservedReboot();
                var session = SafeGetSession();
                report = new AgentReport
                {
                    ComputerName = SystemInfo.ComputerName,
                    DnsName = SystemInfo.DnsName,
                    IpAddresses = SystemInfo.IpAddresses,
                    OsVersion = SystemInfo.OsVersion,
                    AgentVersion = SystemInfo.AgentVersion,
                    BootTime = AgentState.Format(SystemInfo.BootTime),
                    CurrentUser = session?.UserName ?? "",
                    LastUser = SystemInfo.LastLoggedOnUser,
                    State = StateText(),
                    RebootPendingSince = _state.RebootPendingSince.Length > 0 ? _state.RebootPendingSince : _state.ObservedRebootSince,
                    LastError = _workError
                };
                sentScan = _state.ScanUnreported;
                if (sentScan)
                    report.Scan = new UpdateScan { ScannedAt = _state.LastScanAt, Updates = _state.LastScan };
                sentInstall = _state.UnreportedInstall;
                report.Install = sentInstall;
            }

            AgentResponse response;
            try
            {
                response = _server.Report(report);
            }
            catch (Exception ex)
            {
                Log.Warn("Meldung an den Server fehlgeschlagen: " + (ex.InnerException?.Message ?? ex.Message));
                _nextReport = DateTime.Now + ReportInterval;
                return;
            }

            lock (_sync)
            {
                if (sentScan) _state.ScanUnreported = false;
                if (sentInstall != null && ReferenceEquals(_state.UnreportedInstall, sentInstall)) _state.UnreportedInstall = null;
                _state.Save();
            }
            _nextReport = DateTime.Now + ReportInterval;

            // Serverumzug hat Vorrang: nur übernehmen, wenn der neue Server erreichbar ist und sein Zertifikat passt.
            if (!string.IsNullOrEmpty(response.MoveToUrl) && !string.IsNullOrEmpty(response.MoveToCertHash)
                && TryMigrate(response.MoveToUrl, response.MoveToCertHash, report))
                return;

            if (_work != null && _work.IsAlive) return;

            // Offene Läufe bevorzugt über InstallRuns (mit Reboot-Zeit); gelten auch für gruppenlose Clients (manuell).
            var openRuns = response.InstallRuns != null && response.InstallRuns.Count > 0
                ? response.InstallRuns
                : (response.InstallRunIds ?? new List<long>()).Select(id => new InstallRun { Id = id }).ToList();

            if (openRuns.Count > 0)
            {
                StartWork(() => Install(openRuns, response.Group));
            }
            else
            {
                var lastScan = AgentState.Parse(_state.LastScanAt);
                if (response.ScanRequested || lastScan == null || DateTime.Now - lastScan.Value > ScanInterval)
                    StartWork(Scan);
            }
        }

        /// <summary>
        /// Serverumzug: Der (alte) Server weist auf einen neuen. Erst wenn dieser erreichbar ist und sein Zertifikat
        /// zum angegebenen Fingerabdruck passt, werden Adresse und Fingerabdruck in der Registry umgestellt.
        /// </summary>
        private bool TryMigrate(string newUrl, string newHash, AgentReport report)
        {
            newUrl = newUrl.Trim();
            newHash = newHash.Replace(" ", "").ToUpperInvariant();
            string curUrl = AgentPaths.ServerUrl.Trim().TrimEnd('/');
            string curHash = AgentPaths.CertificateHash.Replace(" ", "").ToUpperInvariant();
            if (string.Equals(newUrl.TrimEnd('/'), curUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(newHash, curHash, StringComparison.OrdinalIgnoreCase))
                return false; // bereits auf dem neuen Server

            try
            {
                _server.Report(report, newUrl, newHash); // Prüfung: neuer Server erreichbar, Zertifikat passt
            }
            catch (Exception ex)
            {
                Log.Warn("Serverumzug nach " + newUrl + " noch nicht möglich: " + (ex.InnerException?.Message ?? ex.Message));
                return false;
            }

            AgentPaths.SetServer(newUrl, newHash);
            Log.Info("Serverumzug übernommen: Agent meldet sich künftig an " + newUrl + ".");
            return true;
        }

        private string StateText()
        {
            if (_work != null && _work.IsAlive && _activity.Length > 0) return _activity;
            if (_state.RebootPendingSince.Length > 0)
            {
                var scheduled = AgentState.Parse(_state.RebootScheduledAt);
                return scheduled.HasValue ? "Neustart geplant für " + scheduled.Value.ToString("dd.MM. HH:mm") : "Neustart ausstehend";
            }
            if (_state.ObservedRebootSince.Length > 0) return "Neustart ausstehend";
            return "Bereit";
        }

        private void UpdateObservedReboot()
        {
            bool required = WindowsUpdate.IsRebootRequired();
            if (required && _state.ObservedRebootSince.Length == 0) _state.ObservedRebootSince = AgentState.Format(DateTime.Now);
            if (!required) _state.ObservedRebootSince = "";
        }

        // ------------------------------------------------------------------ Scan und Installation

        private void StartWork(Action action)
        {
            _work = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error("Fehler bei Scan/Installation", ex); }
                finally
                {
                    _activity = "";
                    _nextReport = DateTime.Now; // Ergebnis sofort melden
                    _wake.Set();
                }
            }) { IsBackground = true, Name = "Agent-Arbeit" };
            _work.Start();
        }

        private void Scan()
        {
            _activity = "Sucht nach Updates";
            try
            {
                var updates = WindowsUpdate.Scan();
                lock (_sync)
                {
                    _state.LastScan = updates;
                    _state.LastScanAt = AgentState.Format(DateTime.Now);
                    _state.ScanUnreported = true;
                    _state.Save();
                }
                _workError = "";
                Log.Info("Scan: " + updates.Count + " ausstehende Update(s).");
            }
            catch (Exception ex)
            {
                _workError = "Scan fehlgeschlagen: " + WindowsUpdate.Describe(ex);
                Log.Error("Scan fehlgeschlagen", ex);
                lock (_sync)
                {
                    // Nicht alle 5 Minuten erneut versuchen, sondern erst beim nächsten Scan-Intervall
                    _state.LastScanAt = AgentState.Format(DateTime.Now);
                    _state.Save();
                }
            }
        }

        private void Install(List<InstallRun> runs, int group)
        {
            var runIds = runs.Select(r => r.Id).ToList();
            Log.Info("Installation für Lauf " + string.Join(", ", runIds) + " (Gruppe " + group + ") beginnt.");
            InstallReport report;
            try
            {
                report = WindowsUpdate.InstallAll(text => _activity = text);
                _failedInstallAttempts = 0;
            }
            catch (Exception ex)
            {
                _failedInstallAttempts++;
                string message = WindowsUpdate.Describe(ex);
                _workError = "Installation fehlgeschlagen: " + message;
                Log.Error("Installation fehlgeschlagen (Versuch " + _failedInstallAttempts + ")", ex);
                if (_failedInstallAttempts < 3) return; // beim nächsten Abruf erneut versuchen
                _failedInstallAttempts = 0;
                report = new InstallReport { Success = false, Message = message };
            }
            report.RunIds = runIds;
            Log.Info("Installation: " + report.Message + (report.RebootRequired ? ", Neustart erforderlich" : ""));

            // Neustart-Modus bestimmen: ein manueller Lauf mit Reboot-Zeit übersteuert das Gruppen-Verhalten.
            DateTime? manualRebootAt = null;
            bool hasGroupRun = false;
            foreach (var r in runs)
            {
                if (r.Manual)
                {
                    var at = AgentState.Parse(r.RebootAt);
                    if (at.HasValue && (manualRebootAt == null || at.Value < manualRebootAt.Value)) manualRebootAt = at.Value;
                }
                else hasGroupRun = true;
            }

            lock (_sync)
            {
                _state.UnreportedInstall = report;
                if (report.RebootRequired && _state.RebootPendingSince.Length == 0)
                {
                    _state.ClearReboot();
                    _state.RebootPendingSince = AgentState.Format(DateTime.Now);
                    if (manualRebootAt.HasValue)
                    {
                        _state.RebootGroup = 0; // 0 = manueller/direkter Neustart ohne Zustimmungs-Popup
                        _state.RebootScheduledAt = AgentState.Format(manualRebootAt.Value);
                    }
                    else if (hasGroupRun)
                    {
                        _state.RebootGroup = group;
                    }
                    else
                    {
                        // nur manuelle Läufe ohne Reboot-Zeit → kein erzwungener Neustart, nur als „ausstehend“ sichtbar
                        _state.RebootPendingSince = "";
                    }
                }
                _state.Save();
            }
            _workError = report.Success
                ? (report.FailedCount > 0 ? report.Message : "")
                : "Installation fehlgeschlagen: " + report.Message;

            Scan(); // Stand nach der Installation melden
        }

        // ------------------------------------------------------------------ Neustart-Ablauf

        private void RebootTick()
        {
            lock (_sync)
            {
                var pendingSince = AgentState.Parse(_state.RebootPendingSince);
                if (!pendingSince.HasValue) return;

                if (SystemInfo.BootTime > pendingSince.Value || !WindowsUpdate.IsRebootRequired())
                {
                    Log.Info("Kein Neustart mehr erforderlich – Ablauf beendet.");
                    _state.ClearReboot();
                    _state.Save();
                    return;
                }

                var now = DateTime.Now;
                var initiated = AgentState.Parse(_state.RebootInitiatedAt);
                if (initiated.HasValue && now - initiated.Value < TimeSpan.FromMinutes(10)) return; // Neustart läuft
                // Fester Neustart-Zeitpunkt (manueller Lauf oder Gruppe 2/3 nach Festlegung): dann neu starten,
                // unabhängig davon, ob jemand angemeldet ist. Ist jemand da, 5 Minuten vorher ein Hinweis.
                var scheduled = AgentState.Parse(_state.RebootScheduledAt);
                if (scheduled.HasValue)
                {
                    if (now >= scheduled.Value)
                    {
                        Reboot("geplanter Zeitpunkt erreicht");
                        return;
                    }
                    var sess = SafeGetSession();
                    if (sess != null && !_hintRunning && !_state.CountdownShown && scheduled.Value - now <= CountdownLength)
                    {
                        _state.CountdownShown = true;
                        ShowHint(sess, "countdown " + AgentState.Format(scheduled.Value));
                    }
                    _state.Save();
                    return;
                }

                if (_hintRunning) return;

                var session = SafeGetSession();
                if (session == null)
                {
                    Reboot("niemand angemeldet");
                    return;
                }

                var lastPrompt = AgentState.Parse(_state.LastPromptAt);
                bool promptDue = !lastPrompt.HasValue || now - lastPrompt.Value >= PromptInterval;

                switch (_state.RebootGroup)
                {
                    case 3:
                        // Automatisch nach 5 Minuten, mit Hinweis
                        var at3 = now + CountdownLength;
                        _state.RebootScheduledAt = AgentState.Format(at3);
                        _state.CountdownShown = true;
                        ShowHint(session, "countdown " + AgentState.Format(at3));
                        break;

                    case 2:
                        var deadline = pendingSince.Value + Group2Deadline;
                        if (deadline - now <= CountdownLength)
                        {
                            // Frist läuft ab: Neustart festlegen, Countdown folgt im nächsten Durchlauf
                            _state.RebootScheduledAt = AgentState.Format(deadline > now ? deadline : now + CountdownLength);
                            _state.CountdownShown = false;
                        }
                        else if (promptDue)
                        {
                            _state.LastPromptAt = AgentState.Format(now);
                            ShowHint(session, "frage2 " + AgentState.Format(deadline));
                        }
                        break;

                    default:
                        // Gruppe 1: nur mit Zustimmung, Erinnerung alle 30 Minuten
                        if (promptDue)
                        {
                            _state.LastPromptAt = AgentState.Format(now);
                            ShowHint(session, "frage1");
                        }
                        break;
                }
                _state.Save();
            }
        }

        private void ShowHint(UserSession.ActiveSession session, string arguments)
        {
            if (!File.Exists(AgentPaths.HinweisExe))
            {
                Log.Error("Hinweis-Programm fehlt: " + AgentPaths.HinweisExe);
                return;
            }
            try
            {
                _hintRunning = true;
                UserSession.StartInSession(session.SessionId, AgentPaths.HinweisExe, arguments, OnHintExit);
                Log.Info("Hinweis angezeigt für " + session.UserName + ": " + arguments);
            }
            catch (Exception ex)
            {
                _hintRunning = false;
                Log.Error("Hinweis konnte nicht angezeigt werden", ex);
            }
        }

        private void OnHintExit(int exitCode)
        {
            lock (_sync)
            {
                _hintRunning = false;
                if (_state.RebootPendingSince.Length == 0) return;

                if (exitCode == HintRebootNow)
                {
                    Reboot("vom Benutzer bestätigt");
                }
                else if (exitCode > HintDelayBase && exitCode <= HintDelayBase + 24 * 60)
                {
                    var at = DateTime.Now.AddMinutes(exitCode - HintDelayBase);
                    if (_state.RebootGroup == 2)
                    {
                        var deadline = AgentState.Parse(_state.RebootPendingSince).Value + Group2Deadline;
                        if (at > deadline) at = deadline;
                    }
                    _state.RebootScheduledAt = AgentState.Format(at);
                    _state.CountdownShown = false;
                    Log.Info("Benutzer hat Neustart für " + at.ToString("dd.MM. HH:mm") + " gewählt.");
                }
                _state.Save();
            }
            _wake.Set();
        }

        private void Reboot(string reason)
        {
            Log.Info("Neustart wird ausgelöst (" + reason + ").");
            _state.RebootInitiatedAt = AgentState.Format(DateTime.Now);
            _state.Save();
            try
            {
                var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                    "/r /t 0 /d p:2:17 /c \"DNWsus: Neustart nach Installation von Windows-Updates\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Neustart konnte nicht ausgelöst werden", ex);
            }
        }

        private static UserSession.ActiveSession SafeGetSession()
        {
            try { return UserSession.GetActiveSession(); }
            catch (Exception ex)
            {
                Log.Warn("Angemeldeter Benutzer nicht ermittelbar: " + ex.Message);
                return null;
            }
        }
    }
}
