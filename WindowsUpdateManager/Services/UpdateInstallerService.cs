using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WindowsUpdateManager.Helpers;
using WindowsUpdateManager.Models;

namespace WindowsUpdateManager.Services
{
    public class UpdateInstallerService
    {
        private readonly RemoteSessionService _remoteService;
        private readonly CsvLogger _logger;

        // WUA script runs as SYSTEM via scheduled task.
        // Uses C:\Windows\Temp\ – accessible by both SYSTEM and the WinRM admin session.
        private const string WuaScriptContent =
            "$ErrorActionPreference = 'Stop'\r\n" +
            "$base    = \"$env:SystemRoot\\Temp\"\r\n" +
            "$outFile = \"$base\\wum_result.json\"\r\n" +
            "$installedCount = 0\r\n" +
            "$rebootRequired = $false\r\n" +
            "try {\r\n" +
            "    $kbArray = (Get-Content \"$base\\wum_kblist.txt\" -ErrorAction Stop) -split ','\r\n" +
            "    $session  = New-Object -ComObject Microsoft.Update.Session\r\n" +
            "    $searcher = $session.CreateUpdateSearcher()\r\n" +
            "    $found    = $searcher.Search(\"IsInstalled=0 and Type='Software'\")\r\n" +
            "    $toInstall = New-Object -ComObject Microsoft.Update.UpdateColl\r\n" +
            "    foreach ($u in $found.Updates) {\r\n" +
            "        for ($i = 0; $i -lt $u.KBArticleIDs.Count; $i++) {\r\n" +
            "            if ($kbArray -contains $u.KBArticleIDs.Item($i)) {\r\n" +
            "                $toInstall.Add($u) | Out-Null; break\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "    if ($toInstall.Count -gt 0) {\r\n" +
            "        $dl = $session.CreateUpdateDownloader()\r\n" +
            "        $dl.Updates = $toInstall\r\n" +
            "        $dl.Download() | Out-Null\r\n" +
            "        $inst = $session.CreateUpdateInstaller()\r\n" +
            "        $inst.Updates = $toInstall\r\n" +
            "        $r = $inst.Install()\r\n" +
            "        $rebootRequired = $r.RebootRequired\r\n" +
            "        for ($i = 0; $i -lt $toInstall.Count; $i++) {\r\n" +
            "            if ($r.GetUpdateResult($i).ResultCode -eq 2) { $installedCount++ }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "    # Trigger WU re-detection so the GUI reflects the new state\r\n" +
            "    try { $au = New-Object -ComObject Microsoft.Update.AutoUpdate; $au.DetectNow() } catch {}\r\n" +
            "    [PSCustomObject]@{ Success=$true; InstalledCount=$installedCount; RebootRequired=$rebootRequired; Error='' } | ConvertTo-Json | Out-File $outFile -Encoding UTF8 -Force\r\n" +
            "} catch {\r\n" +
            "    [PSCustomObject]@{ Success=$false; InstalledCount=0; RebootRequired=$false; Error=$_.Exception.Message } | ConvertTo-Json | Out-File $outFile -Encoding UTF8 -Force\r\n" +
            "}\r\n";

        // Orchestrator: drops KB list + WUA script, fires scheduled task as SYSTEM, waits, reads result.
        private const string InstallScriptBody =
            "$ErrorActionPreference = 'Stop'\r\n" +
            "$taskName = 'WUM_' + [System.Guid]::NewGuid().ToString('N').Substring(0,8)\r\n" +
            "$base    = \"$env:SystemRoot\\Temp\"\r\n" +
            "$kbFile  = \"$base\\wum_kblist.txt\"\r\n" +
            "$ps1File = \"$base\\wum_install.ps1\"\r\n" +
            "$outFile = \"$base\\wum_result.json\"\r\n" +
            "try {\r\n" +
            "    [System.IO.File]::WriteAllText($kbFile,  $KBList,    [System.Text.Encoding]::UTF8)\r\n" +
            "    [System.IO.File]::WriteAllText($ps1File, $WuaScript, [System.Text.Encoding]::UTF8)\r\n" +
            "    if (Test-Path $outFile) { Remove-Item $outFile -Force }\r\n" +
            "    $psExe = \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"\r\n" +
            "    $arg   = \"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File `\"$ps1File`\"\"\r\n" +
            "    $action    = New-ScheduledTaskAction -Execute $psExe -Argument $arg\r\n" +
            "    $settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 60) -MultipleInstances IgnoreNew\r\n" +
            "    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest\r\n" +
            "    Register-ScheduledTask -TaskName $taskName -Action $action -Settings $settings -Principal $principal -Force | Out-Null\r\n" +
            "    Start-ScheduledTask -TaskName $taskName\r\n" +
            "    $timeout = 3600; $waited = 0; $done = $false\r\n" +
            "    while ($waited -lt $timeout) {\r\n" +
            "        Start-Sleep -Seconds 5; $waited += 5\r\n" +
            "        $info = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue\r\n" +
            "        if ($info -eq $null -or $info.State -eq 'Ready') { $done = $true; break }\r\n" +
            "    }\r\n" +
            "    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue\r\n" +
            "    if (-not $done) { throw 'Timeout nach 60 Minuten.' }\r\n" +
            "    if (Test-Path $outFile) { Get-Content $outFile -Raw -Encoding UTF8 }\r\n" +
            "    else { throw 'Ergebnisdatei fehlt – WUA-Script wurde nicht ausgefuehrt.' }\r\n" +
            "} catch {\r\n" +
            "    [PSCustomObject]@{ Success=$false; InstalledCount=0; RebootRequired=$false; Error=$_.Exception.Message } | ConvertTo-Json\r\n" +
            "} finally {\r\n" +
            "    Remove-Item $kbFile  -Force -ErrorAction SilentlyContinue\r\n" +
            "    Remove-Item $ps1File -Force -ErrorAction SilentlyContinue\r\n" +
            "    Remove-Item $outFile -Force -ErrorAction SilentlyContinue\r\n" +
            "}\r\n";

        // Scheduled install: writes files immediately, registers task with future trigger, returns without waiting.
        // Files in C:\Windows\Temp persist until the task runs. No result is read back.
        private const string ScheduledInstallScriptBody =
            "$ErrorActionPreference = 'Stop'\r\n" +
            "$base     = \"$env:SystemRoot\\Temp\"\r\n" +
            "$kbFile   = \"$base\\wum_kblist.txt\"\r\n" +
            "$ps1File  = \"$base\\wum_install.ps1\"\r\n" +
            "$taskName = 'WUM_Sched_' + [System.Guid]::NewGuid().ToString('N').Substring(0,8)\r\n" +
            "try {\r\n" +
            "    [System.IO.File]::WriteAllText($kbFile,  $KBList,    [System.Text.Encoding]::UTF8)\r\n" +
            "    [System.IO.File]::WriteAllText($ps1File, $WuaScript, [System.Text.Encoding]::UTF8)\r\n" +
            "    $psExe    = \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"\r\n" +
            "    $arg      = \"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File `\"$ps1File`\"\"\r\n" +
            "    $action   = New-ScheduledTaskAction -Execute $psExe -Argument $arg\r\n" +
            "    $at       = [datetime]::ParseExact($ScheduleAt, 'yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture)\r\n" +
            "    $trigger  = New-ScheduledTaskTrigger -Once -At $at\r\n" +
            "    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 120) -StartWhenAvailable\r\n" +
            "    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest\r\n" +
            "    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null\r\n" +
            "    'OK'\r\n" +
            "} catch {\r\n" +
            "    throw $_\r\n" +
            "}\r\n";

        public UpdateInstallerService(RemoteSessionService remoteService, CsvLogger logger)
        {
            if (remoteService == null) throw new ArgumentNullException("remoteService");
            if (logger == null)        throw new ArgumentNullException("logger");
            _remoteService = remoteService;
            _logger        = logger;
        }

        public async Task InstallUpdatesAsync(
            List<ServerInfo> servers,
            IProgress<ServerProgressPair> progress,
            int maxParallel,
            CancellationToken cancellationToken,
            bool honorSelectedFlag = true)
        {
            var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
            var tasks = new List<Task>();

            foreach (var server in servers)
            {
                if (honorSelectedFlag && !server.Selected) continue;

                cancellationToken.ThrowIfCancellationRequested();

                var capturedServer = server;
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        List<UpdateInfo> selectedUpdates = capturedServer.AvailableUpdates.FindAll(u => u.Selected);
                        if (selectedUpdates.Count == 0)
                        {
                            capturedServer.Status = ServerStatus.Done;
                            capturedServer.StatusMessage = "Keine Updates ausgewählt";
                            if (progress != null) progress.Report(new ServerProgressPair(capturedServer, null));
                            return;
                        }

                        // Build KB list
                        var kbs = new List<string>();
                        foreach (var upd in selectedUpdates)
                        {
                            if (!string.IsNullOrEmpty(upd.KBArticleId))
                                kbs.Add(upd.KBArticleId);
                        }
                        string kbList = string.Join(",", kbs.ToArray());

                        string escapedWua = WuaScriptContent.Replace("'", "''");

                        if (capturedServer.InstallScheduledAt.HasValue)
                        {
                            string scheduleAt = capturedServer.InstallScheduledAt.Value
                                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                            string scheduledScript =
                                "$KBList    = '" + kbList + "'\r\n" +
                                "$WuaScript = '" + escapedWua + "'\r\n" +
                                "$ScheduleAt = '" + scheduleAt + "'\r\n" +
                                ScheduledInstallScriptBody;

                            capturedServer.Status = ServerStatus.Scheduled;
                            capturedServer.StatusMessage = "Geplant: " + capturedServer.InstallScheduledAt.Value.ToString("dd.MM.yyyy HH:mm");
                            if (progress != null) progress.Report(new ServerProgressPair(capturedServer, null));
                            await _remoteService.InvokeScriptAsync(capturedServer.Name, scheduledScript, 60);
                            return;
                        }

                        capturedServer.Status = ServerStatus.Installing;
                        capturedServer.StatusMessage = "Installiere...";
                        if (progress != null) progress.Report(new ServerProgressPair(capturedServer, null));

                        string script =
                            "$KBList    = '" + kbList + "'\r\n" +
                            "$WuaScript = '" + escapedWua + "'\r\n" +
                            InstallScriptBody;

                        string json = await _remoteService.InvokeScriptAsync(capturedServer.Name, script, 600);

                        InstallJsonResult installResult = ParseInstallResult(json);

                        // Report per update
                        foreach (var upd in selectedUpdates)
                        {
                            var ir = new InstallResult();
                            ir.ServerName       = capturedServer.Name;
                            ir.KBArticleId      = upd.KBArticleId;
                            ir.UpdateTitle      = upd.Title;
                            ir.SizeMB           = upd.SizeMB;
                            ir.Success          = installResult.Success;
                            ir.ErrorMessage     = installResult.Success ? null : installResult.Error;
                            ir.Timestamp        = DateTime.Now;
                            ir.RebootScheduledAt = capturedServer.RebootScheduledAt;

                            _logger.Log(ir);
                            if (progress != null) progress.Report(new ServerProgressPair(capturedServer, ir));
                        }

                        // Reboot handling: scheduled time wins; otherwise reboot immediately if required
                        bool immediateReboot = false;
                        string rebootError    = null;
                        if (capturedServer.RebootScheduledAt.HasValue)
                        {
                            string rebootAt   = capturedServer.RebootScheduledAt.Value.ToString("yyyy-MM-dd HH:mm:ss");
                            string safeServer = capturedServer.Name.Replace(' ', '_').Replace('\\', '_');
                            string rebootScript =
                                "$ErrorActionPreference = 'Stop'\r\n" +
                                "$taskName  = 'WUM_Reboot_" + safeServer + "'\r\n" +
                                "Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue\r\n" +
                                "$psExe    = \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"\r\n" +
                                "$action   = New-ScheduledTaskAction -Execute $psExe -Argument '-NonInteractive -NoProfile -Command \"Restart-Computer -Force\"'\r\n" +
                                "$at        = [datetime]::ParseExact('" + rebootAt + "', 'yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture)\r\n" +
                                "$trigger   = New-ScheduledTaskTrigger -Once -At $at\r\n" +
                                "$settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5) -StartWhenAvailable\r\n" +
                                "$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest\r\n" +
                                "Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null\r\n" +
                                "'Reboot scheduled for " + rebootAt + "'\r\n";
                            try
                            {
                                await _remoteService.InvokeScriptAsync(capturedServer.Name, rebootScript, 30);
                            }
                            catch (Exception rex)
                            {
                                rebootError = rex.Message;
                            }
                        }
                        else if (installResult.RebootRequired)
                        {
                            // No explicit time set → reboot immediately via SYSTEM scheduled task
                            string immediateScript =
                                "$ErrorActionPreference = 'Stop'\r\n" +
                                "Unregister-ScheduledTask -TaskName 'WUM_ImmediateReboot' -Confirm:$false -ErrorAction SilentlyContinue\r\n" +
                                "$psExe    = \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"\r\n" +
                                "$action   = New-ScheduledTaskAction -Execute $psExe -Argument '-NonInteractive -NoProfile -Command \"Restart-Computer -Force\"'\r\n" +
                                "$trigger   = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(10)\r\n" +
                                "$settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5) -StartWhenAvailable\r\n" +
                                "$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest\r\n" +
                                "Register-ScheduledTask -TaskName 'WUM_ImmediateReboot' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null\r\n";
                            try
                            {
                                await _remoteService.InvokeScriptAsync(capturedServer.Name, immediateScript, 30);
                                immediateReboot = true;
                            }
                            catch (Exception rex)
                            {
                                rebootError = rex.Message;
                            }
                        }

                        capturedServer.Status = installResult.Success ? ServerStatus.Done : ServerStatus.Error;
                        if (installResult.Success)
                        {
                            capturedServer.StatusMessage = installResult.InstalledCount + " Update(s) installiert";
                            if (immediateReboot)
                                capturedServer.StatusMessage += ", Neustart eingeleitet";
                            else if (rebootError != null)
                                capturedServer.StatusMessage += " (Reboot-Fehler: " + rebootError + ")";
                        }
                        else
                        {
                            capturedServer.StatusMessage = "Fehler: " + installResult.Error;
                        }
                        if (!installResult.Success)
                            capturedServer.ErrorMessage = installResult.Error;

                        if (progress != null) progress.Report(new ServerProgressPair(capturedServer, null));
                    }
                    catch (OperationCanceledException)
                    {
                        capturedServer.Status = ServerStatus.Unknown;
                        capturedServer.StatusMessage = "Abgebrochen";
                        if (progress != null) progress.Report(new ServerProgressPair(capturedServer, null));
                    }
                    catch (Exception ex)
                    {
                        capturedServer.Status = ServerStatus.Error;
                        capturedServer.ErrorMessage = ex.Message;
                        capturedServer.StatusMessage = "Fehler bei Installation";

                        // Log each selected update as failed so the CSV always has entries
                        var failedUpdates = capturedServer.AvailableUpdates.FindAll(u => u.Selected);
                        foreach (var upd in failedUpdates)
                        {
                            var ir = new InstallResult();
                            ir.ServerName    = capturedServer.Name;
                            ir.KBArticleId   = upd.KBArticleId;
                            ir.UpdateTitle   = upd.Title;
                            ir.SizeMB        = upd.SizeMB;
                            ir.Success       = false;
                            ir.ErrorMessage  = ex.Message;
                            ir.Timestamp     = DateTime.Now;
                            _logger.Log(ir);
                            if (progress != null) progress.Report(new ServerProgressPair(capturedServer, ir));
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        private static InstallJsonResult ParseInstallResult(string json)
        {
            var defaultResult = new InstallJsonResult();
            defaultResult.Success = false;
            defaultResult.Error   = "Ungültige Antwort vom Server";

            if (string.IsNullOrWhiteSpace(json))
                return defaultResult;

            try
            {
                var serializer = new JavaScriptSerializer();
                string trimmed = json.Trim();

                Dictionary<string, object> dict;
                if (trimmed.StartsWith("["))
                {
                    var list = serializer.Deserialize<List<Dictionary<string, object>>>(trimmed);
                    dict = (list != null && list.Count > 0) ? list[0] : null;
                }
                else
                {
                    dict = serializer.Deserialize<Dictionary<string, object>>(trimmed);
                }

                if (dict == null) return defaultResult;

                var result = new InstallJsonResult();
                result.Success        = GetBool(dict, "Success");
                result.InstalledCount = GetInt(dict, "InstalledCount");
                result.RebootRequired = GetBool(dict, "RebootRequired");
                result.Error          = GetString(dict, "Error");
                return result;
            }
            catch
            {
                return defaultResult;
            }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
                return val.ToString();
            return "";
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                if (val is bool) return (bool)val;
                bool parsed;
                if (bool.TryParse(val.ToString(), out parsed)) return parsed;
                string s = val.ToString();
                if (s == "1" || s.ToLower() == "true") return true;
            }
            return false;
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                int i;
                if (int.TryParse(val.ToString(), out i)) return i;
            }
            return 0;
        }

        private class InstallJsonResult
        {
            public bool   Success        { get; set; }
            public int    InstalledCount { get; set; }
            public bool   RebootRequired { get; set; }
            public string Error          { get; set; }
        }
    }

    /// <summary>
    /// Simple pair to pass server + install result through IProgress without C# 7 tuples.
    /// </summary>
    public class ServerProgressPair
    {
        public ServerProgressPair(ServerInfo server, InstallResult result)
        {
            Server = server;
            Result = result;
        }

        public ServerInfo   Server { get; private set; }
        public InstallResult Result { get; private set; }
    }
}
