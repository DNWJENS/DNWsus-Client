using System;
using System.IO;
using System.Text;
using WindowsUpdateManager.Models;

namespace WindowsUpdateManager.Helpers
{
    public class CsvLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        private const string CsvHeader = "Timestamp,Server,KB,UpdateTitle,SizeMB,Status,RebootScheduled";

        public string LogFilePath
        {
            get { return _logFilePath; }
        }

        public CsvLogger(string logDirectory)
        {
            // Fallback to Desktop if the requested directory can't be created
            try
            {
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);
            }
            catch
            {
                logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DNWsus-Client-Logs");
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);
            }

            string fileName = "dnwsus-client_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
            _logFilePath = Path.Combine(logDirectory, fileName);

            try
            {
                lock (_lock)
                {
                    if (!File.Exists(_logFilePath) || new FileInfo(_logFilePath).Length == 0)
                        File.WriteAllText(_logFilePath, CsvHeader + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // If header write fails, logging will be skipped silently per entry
            }
        }

        public void Log(InstallResult result)
        {
            if (result == null) return;

            lock (_lock)
            {
                try
                {
                    string rebootScheduled = result.RebootScheduledAt.HasValue
                        ? result.RebootScheduledAt.Value.ToString("dd.MM.yyyy HH:mm")
                        : "";

                    string status = result.Success ? "Erfolg" : "Fehler";
                    if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                        status += ": " + EscapeCsv(result.ErrorMessage);

                    string line = string.Join(",",
                        new string[]
                        {
                            EscapeCsv(result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                            EscapeCsv(result.ServerName ?? ""),
                            EscapeCsv(result.KBArticleId ?? ""),
                            EscapeCsv(result.UpdateTitle ?? ""),
                            result.SizeMB.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            EscapeCsv(status),
                            EscapeCsv(rebootScheduled)
                        });

                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Best-effort logging
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuoting = value.Contains(",") || value.Contains("\"") ||
                                value.Contains("\n") || value.Contains("\r");
            if (needsQuoting)
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}
