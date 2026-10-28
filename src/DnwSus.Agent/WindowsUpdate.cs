using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DnwSus.Agent
{
    /// <summary>
    /// Suche und Installation über die Windows-Update-Schnittstelle (WUA-COM, wie in der Windows-Update-Einstellungsseite).
    /// Kein PowerShell: die COM-Objekte werden direkt angesprochen.
    /// Updatequelle ist bewusst immer **Windows Update direkt** (ServerSelection = ssWindowsUpdate), unabhängig von
    /// einer eventuell per GPO gesetzten WSUS-Zuweisung. Sonst läuft die Abfrage in einen (oft unerreichbaren) WSUS
    /// und bricht mit Timeout 0x80072EE2 ab. DNWsus steuert die Updates selbst, deshalb WSUS hier ignorieren.
    /// </summary>
    internal static class WindowsUpdate
    {
        private const string Criteria = "IsInstalled=0 and Type='Software' and IsHidden=0";

        // ServerSelection-Werte der WUA: 0=Default, 1=ManagedServer (WSUS), 2=WindowsUpdate, 3=Others
        private const int SsWindowsUpdate = 2;

        private static dynamic CreateSearcher(dynamic session)
        {
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = SsWindowsUpdate; // direkt Windows Update, WSUS-Policy ignorieren
            return searcher;
        }

        public static List<UpdateItem> Scan()
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "DNWsus Agent";
            dynamic searcher = CreateSearcher(session);
            dynamic result = searcher.Search(Criteria);

            var list = new List<UpdateItem>();
            for (int i = 0; i < result.Updates.Count; i++)
                list.Add(ToItem(result.Updates.Item(i)));
            return list;
        }

        /// <summary>
        /// Lädt ausstehende Updates herunter und installiert sie. <paramref name="onlyClasses"/> beschränkt auf bestimmte
        /// Klassen (null oder leer = alle). Die tatsächlich installierten Klassen stehen danach in <see cref="InstallReport.InstalledClasses"/>.
        /// </summary>
        public static InstallReport InstallAll(ICollection<string> onlyClasses, Action<string> setState)
        {
            var report = new InstallReport();
            bool filter = onlyClasses != null && onlyClasses.Count > 0;

            setState("Sucht nach Updates");
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "DNWsus Agent";
            dynamic searcher = CreateSearcher(session);
            dynamic found = searcher.Search(Criteria);

            dynamic toInstall = CreateCom("Microsoft.Update.UpdateColl");
            for (int i = 0; i < found.Updates.Count; i++)
            {
                dynamic update = found.Updates.Item(i);
                var item = ToItem(update);
                if (filter && !onlyClasses.Contains(item.Class)) continue; // Klasse nicht freigegeben
                // Updates, die eine Eingabe des Benutzers erwarten, lassen sich unbeaufsichtigt nicht installieren.
                if ((bool)update.InstallationBehavior.CanRequestUserInput)
                {
                    report.Items.Add(new InstallItem { KB = item.KB, Title = item.Title, Success = false, Result = "übersprungen (erfordert Benutzereingabe)" });
                    continue;
                }
                if (!(bool)update.EulaAccepted) update.AcceptEula();
                toInstall.Add(update);
            }

            if (toInstall.Count == 0)
            {
                report.Success = true;
                report.Message = "Keine Updates zu installieren.";
                return report;
            }

            setState("Lädt " + toInstall.Count + " Update(s) herunter");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = toInstall;
            downloader.Download();

            dynamic downloaded = CreateCom("Microsoft.Update.UpdateColl");
            for (int i = 0; i < toInstall.Count; i++)
            {
                dynamic update = toInstall.Item(i);
                if ((bool)update.IsDownloaded) downloaded.Add(update);
                else
                {
                    var item = ToItem(update);
                    report.Items.Add(new InstallItem { KB = item.KB, Title = item.Title, Success = false, Result = "Download fehlgeschlagen" });
                }
            }

            if (downloaded.Count > 0)
            {
                setState("Installiert " + downloaded.Count + " Update(s)");
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = downloaded;
                dynamic result = installer.Install();
                report.RebootRequired = (bool)result.RebootRequired;

                for (int i = 0; i < downloaded.Count; i++)
                {
                    var item = ToItem(downloaded.Item(i));
                    dynamic updateResult = result.GetUpdateResult(i);
                    int code = (int)updateResult.ResultCode;
                    bool ok = code == 2 || code == 3; // 2 = erfolgreich, 3 = erfolgreich mit Fehlern
                    report.Items.Add(new InstallItem
                    {
                        KB = item.KB,
                        Title = item.Title,
                        Success = ok,
                        Result = ok ? "installiert" : ResultText(code) + " (0x" + ((int)updateResult.HResult).ToString("X8") + ")"
                    });
                    if (ok && !report.InstalledClasses.Contains(item.Class)) report.InstalledClasses.Add(item.Class);
                }
            }

            foreach (var item in report.Items)
            {
                if (item.Success) report.InstalledCount++;
                else report.FailedCount++;
            }
            report.RebootRequired = report.RebootRequired || IsRebootRequired();
            report.Success = report.InstalledCount > 0 || report.FailedCount == 0;
            report.Message = report.InstalledCount + " installiert, " + report.FailedCount + " fehlgeschlagen";
            return report;
        }

        /// <summary>Wartet Windows auf einen Neustart, damit installierte Updates wirksam werden?</summary>
        public static bool IsRebootRequired()
        {
            try
            {
                dynamic info = CreateCom("Microsoft.Update.SystemInfo");
                return (bool)info.RebootRequired;
            }
            catch (Exception ex)
            {
                Log.Warn("Neustart-Status nicht abfragbar: " + ex.Message);
                return false;
            }
        }

        private static UpdateItem ToItem(dynamic update)
        {
            string kb = update.KBArticleIDs.Count > 0 ? (string)update.KBArticleIDs.Item(0) : "";
            var categories = new List<string>();
            for (int c = 0; c < update.Categories.Count; c++)
                categories.Add((string)update.Categories.Item(c).Name);
            int rebootBehavior = (int)update.InstallationBehavior.RebootBehavior;
            return new UpdateItem
            {
                KB = kb,
                Title = (string)update.Title,
                SizeMB = Math.Round(Convert.ToDouble(update.MaxDownloadSize) / 1048576.0, 1),
                RebootRequired = rebootBehavior != 0,
                Category = categories.Count > 0 ? categories[0] : "",
                Class = UpdateClassifier.Classify(categories)
            };
        }

        private static string ResultText(int code)
        {
            switch (code)
            {
                case 0: return "nicht gestartet";
                case 1: return "läuft noch";
                case 4: return "fehlgeschlagen";
                case 5: return "abgebrochen";
                default: return "Ergebnis " + code;
            }
        }

        private static dynamic CreateCom(string progId)
        {
            var type = Type.GetTypeFromProgID(progId, true);
            return Activator.CreateInstance(type);
        }

        public static string Describe(Exception ex) =>
            ex is COMException com ? ex.Message + " (0x" + com.ErrorCode.ToString("X8") + ")" : ex.Message;
    }
}
