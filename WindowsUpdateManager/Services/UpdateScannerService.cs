using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WindowsUpdateManager.Models;

namespace WindowsUpdateManager.Services
{
    public class UpdateScannerService
    {
        private readonly RemoteSessionService _remoteService;

        private const string ScanScript =
            "$ErrorActionPreference = 'Stop'\r\n" +
            "try {\r\n" +
            "    $os   = Get-WmiObject Win32_OperatingSystem\r\n" +
            "    $span = (Get-Date) - $os.ConvertToDateTime($os.LastBootUpTime)\r\n" +
            "    $uptime = '' + $span.Days + 'd ' + $span.Hours + 'h ' + $span.Minutes + 'm'\r\n" +
            "    $s = New-Object -ComObject Microsoft.Update.Session\r\n" +
            "    $searcher = $s.CreateUpdateSearcher()\r\n" +
            "    $result = $searcher.Search(\"IsInstalled=0 and Type='Software'\")\r\n" +
            "    $updates = @()\r\n" +
            "    foreach ($u in $result.Updates) {\r\n" +
            "        $kbs = @(); for ($i=0;$i -lt $u.KBArticleIDs.Count;$i++){$kbs+=$u.KBArticleIDs.Item($i)}\r\n" +
            "        $rb = $u.InstallationBehavior.RebootBehavior\r\n" +
            "        $cat = if($u.Categories.Count -gt 0){$u.Categories.Item(0).Name}else{'Other'}\r\n" +
            "        $updates += [PSCustomObject]@{\r\n" +
            "            Title=$u.Title\r\n" +
            "            KBArticleId=if($kbs.Count -gt 0){$kbs[0]}else{''}\r\n" +
            "            SizeMB=[math]::Round($u.MaxDownloadSize/1MB,2)\r\n" +
            "            RebootRequired=($rb -eq 1 -or $rb -eq 2)\r\n" +
            "            Category=$cat\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "    [PSCustomObject]@{ Uptime=$uptime; Updates=$updates } | ConvertTo-Json -Depth 3\r\n" +
            "} catch { Write-Error $_.Exception.Message }\r\n";

        public UpdateScannerService(RemoteSessionService remoteService)
        {
            if (remoteService == null) throw new ArgumentNullException("remoteService");
            _remoteService = remoteService;
        }

        public async Task ScanServersAsync(
            List<ServerInfo> servers,
            IProgress<ServerInfo> progress,
            int maxParallel,
            CancellationToken cancellationToken)
        {
            const int scanTimeoutSeconds = 180;

            var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
            var tasks = new List<Task>();

            foreach (var server in servers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capturedServer = server;
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        bool isOnline = await _remoteService.TestConnectionAsync(capturedServer.Name);
                        if (!isOnline)
                        {
                            capturedServer.Status = ServerStatus.Offline;
                            capturedServer.StatusMessage = "Nicht erreichbar";
                            if (progress != null) progress.Report(capturedServer);
                            return;
                        }

                        capturedServer.Status = ServerStatus.Scanning;
                        capturedServer.StatusMessage = "Wird gescannt...";
                        if (progress != null) progress.Report(capturedServer);

                        string json = await _remoteService.InvokeScriptAsync(capturedServer.Name, ScanScript, scanTimeoutSeconds);

                        string uptime;
                        capturedServer.AvailableUpdates = ParseScanResult(json, out uptime);
                        capturedServer.Uptime = uptime;
                        capturedServer.Status = ServerStatus.Online;
                        capturedServer.StatusMessage = capturedServer.AvailableUpdates.Count + " Update(s) verfügbar";
                        if (progress != null) progress.Report(capturedServer);
                    }
                    catch (OperationCanceledException)
                    {
                        capturedServer.Status = ServerStatus.Unknown;
                        capturedServer.StatusMessage = "Abgebrochen";
                        if (progress != null) progress.Report(capturedServer);
                    }
                    catch (Exception ex)
                    {
                        capturedServer.Status = ServerStatus.Error;
                        capturedServer.ErrorMessage = ex.Message;
                        capturedServer.StatusMessage = "Fehler beim Scannen";
                        if (progress != null) progress.Report(capturedServer);
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

        private static List<UpdateInfo> ParseScanResult(string json, out string uptime)
        {
            uptime = "—";
            var updates = new List<UpdateInfo>();

            if (string.IsNullOrWhiteSpace(json)) return updates;
            json = json.Trim();

            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;

                // Expect wrapper: { "Uptime": "...", "Updates": [...] }
                var wrapper = serializer.Deserialize<Dictionary<string, object>>(json);
                if (wrapper == null) return updates;

                if (wrapper.ContainsKey("Uptime") && wrapper["Uptime"] != null)
                    uptime = wrapper["Uptime"].ToString();

                if (!wrapper.ContainsKey("Updates") || wrapper["Updates"] == null)
                    return updates;

                // Updates can be an array or a single object (PS ConvertTo-Json quirk)
                var rawUpdates = wrapper["Updates"];
                var updateList = rawUpdates as System.Collections.ArrayList;

                if (updateList != null)
                {
                    foreach (var item in updateList)
                    {
                        var dict = item as Dictionary<string, object>;
                        if (dict != null) updates.Add(MapDictToUpdateInfo(dict));
                    }
                }
                else
                {
                    // Single update returned as object, not array
                    var dict = rawUpdates as Dictionary<string, object>;
                    if (dict != null) updates.Add(MapDictToUpdateInfo(dict));
                }
            }
            catch
            {
                // Return what we have on parse error
            }

            return updates;
        }

        private static UpdateInfo MapDictToUpdateInfo(Dictionary<string, object> dict)
        {
            var u = new UpdateInfo();
            u.Title          = GetString(dict, "Title");
            u.KBArticleId    = GetString(dict, "KBArticleId");
            u.SizeMB         = GetDouble(dict, "SizeMB");
            u.RebootRequired = GetBool(dict, "RebootRequired");
            u.Category       = GetString(dict, "Category");
            u.Selected       = true;
            return u;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
                return val.ToString();
            return "";
        }

        private static double GetDouble(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                double d;
                if (double.TryParse(val.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out d))
                    return d;
            }
            return 0.0;
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
    }
}
