using System;
using System.IO;
using System.Text;

namespace DnwSus.Agent
{
    /// <summary>Einfaches Logfile unter C:\ProgramData\DNWsus\Agent\agent.log (max. 2 MB, danach agent.old.log).</summary>
    internal static class Log
    {
        private static readonly object Lock = new object();
        private const long MaxSize = 2 * 1024 * 1024;

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);
        public static void Error(string message, Exception ex = null) =>
            Write("FEHLER", ex == null ? message : message + ": " + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    string path = Path.Combine(AgentPaths.DataDirectory, "agent.log");
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxSize)
                    {
                        string old = Path.Combine(AgentPaths.DataDirectory, "agent.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging darf den Agenten nie stoppen.
            }
        }
    }
}
