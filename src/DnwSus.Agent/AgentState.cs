using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DnwSus.Agent
{
    /// <summary>
    /// Zustand, der einen Neustart des Dienstes überstehen muss (C:\ProgramData\DNWsus\Agent\state.json):
    /// laufender Neustart-Ablauf, letzte Scan-Ergebnisse, noch nicht zugestellte Installationsberichte.
    /// Zeitangaben als String, damit die Datei lesbar bleibt.
    /// </summary>
    internal sealed class AgentState
    {
        // ---- Neustart-Ablauf ----
        /// <summary>Seit wann (nach einer Installation durch den Agenten) ein Neustart aussteht.</summary>
        public string RebootPendingSince { get; set; } = "";

        /// <summary>Gruppe zum Zeitpunkt der Installation; bestimmt das Neustart-Verhalten.</summary>
        public int RebootGroup { get; set; }

        /// <summary>Vom Benutzer gewählter bzw. automatisch festgelegter Neustart-Zeitpunkt.</summary>
        public string RebootScheduledAt { get; set; } = "";

        public string LastPromptAt { get; set; } = "";
        public bool CountdownShown { get; set; }
        public string RebootInitiatedAt { get; set; } = "";

        /// <summary>Seit wann Windows einen Neustart verlangt, auch wenn ihn nicht der Agent ausgelöst hat (nur zur Anzeige).</summary>
        public string ObservedRebootSince { get; set; } = "";

        // ---- Scan/Installation ----
        public string LastScanAt { get; set; } = "";
        public List<UpdateItem> LastScan { get; set; } = new List<UpdateItem>();
        public bool ScanUnreported { get; set; }
        public InstallReport UnreportedInstall { get; set; }

        // ---- Hilfen ----
        public static DateTime? Parse(string value) =>
            DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : (DateTime?)null;

        public static string Format(DateTime? value) =>
            value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) ?? "";

        public void ClearReboot()
        {
            RebootPendingSince = "";
            RebootGroup = 0;
            RebootScheduledAt = "";
            LastPromptAt = "";
            CountdownShown = false;
            RebootInitiatedAt = "";
        }

        private static string FilePath => Path.Combine(AgentPaths.DataDirectory, "state.json");

        public static AgentState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return new JavaScriptSerializer().Deserialize<AgentState>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new AgentState();
            }
            catch (Exception ex)
            {
                Log.Warn("state.json nicht lesbar, beginne neu: " + ex.Message);
            }
            return new AgentState();
        }

        public void Save()
        {
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("state.json konnte nicht gespeichert werden", ex);
            }
        }
    }
}
