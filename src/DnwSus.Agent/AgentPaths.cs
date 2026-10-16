using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace DnwSus.Agent
{
    /// <summary>Pfade und Konfiguration des Agenten.</summary>
    internal static class AgentPaths
    {
        /// <summary>Registry-Schlüssel, in den die MSI Server-Adresse und Zertifikats-Fingerabdruck schreibt.</summary>
        public const string ConfigKey = @"SOFTWARE\DNWsus\Agent";

        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DNWsus", "Agent");

        public static readonly string InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;

        public static string HinweisExe => Path.Combine(InstallDirectory, "DnwSus.Hinweis.exe");

        public static string ServerUrl => ReadConfig("ServerUrl");

        public static string CertificateHash => ReadConfig("CertHash");

        private static string ReadConfig(string name)
        {
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(ConfigKey))
                return (key?.GetValue(name) as string ?? "").Trim();
        }

        /// <summary>
        /// Datenordner anlegen: SYSTEM und Administratoren Vollzugriff, Benutzer nur lesen
        /// (sonst könnte ein Benutzer die Statusdatei des Agenten manipulieren).
        /// </summary>
        public static void EnsureDataDirectory()
        {
            var dir = new DirectoryInfo(DataDirectory);
            if (!dir.Exists) dir.Create();

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            dir.SetAccessControl(security);
        }
    }
}
