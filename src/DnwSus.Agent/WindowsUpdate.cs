using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DnwSus.Agent
{
    /// <summary>
    /// Suche und Installation über die Windows-Update-Schnittstelle (WUA-COM, wie in der Windows-Update-Einstellungsseite).
    /// Kein PowerShell: die COM-Objekte werden direkt angesprochen. Updatequelle ist, was am Client eingestellt ist
    /// (Microsoft Update oder WSUS).
    /// </summary>
    internal static class WindowsUpdate
    {
        private const string Criteria = "IsInstalled=0 and Type='Software' and IsHidden=0";

        public static List<UpdateItem> Scan()
        {
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "DNWsus Agent";
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search(Criteria);

            var list = new List<UpdateItem>();
            for (int i = 0; i < result.Updates.Count; i++)
                list.Add(ToItem(result.Updates.Item(i)));
            return list;
        }

        /// <summary>Lädt alle ausstehenden Updates herunter und installiert sie.</summary>
        public static InstallReport InstallAll(Action<string> setState)
        {
            var report = new InstallReport();

            setState("Sucht nach Updates");
            dynamic session = CreateCom("Microsoft.Update.Session");
            session.ClientApplicationID = "DNWsus Agent";
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic found = searcher.Search(Criteria);

            dynamic toInstall = CreateCom("Microsoft.Update.UpdateColl");
            for (int i = 0; i < found.Updates.Count; i++)
            {
                dynamic update = found.Updates.Item(i);
                // Updates, die eine Eingabe des Benutzers erwarten, lassen sich unbeaufsichtigt nicht installieren.
                if ((bool)update.InstallationBehavior.CanRequestUserInput)
                {
                    var item = ToItem(update);
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
            string category = update.Categories.Count > 0 ? (string)update.Categories.Item(0).Name : "";
            int rebootBehavior = (int)update.InstallationBehavior.RebootBehavior;
            return new UpdateItem
            {
                KB = kb,
                Title = (string)update.Title,
                SizeMB = Math.Round(Convert.ToDouble(update.MaxDownloadSize) / 1048576.0, 1),
                RebootRequired = rebootBehavior != 0,
                Category = category
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
