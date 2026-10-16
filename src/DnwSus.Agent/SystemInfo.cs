using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DnwSus.Agent
{
    /// <summary>Angaben über diesen PC für die Statusmeldung.</summary>
    internal static class SystemInfo
    {
        public static string ComputerName => Environment.MachineName.ToUpperInvariant();

        public static string DnsName
        {
            get
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                return string.IsNullOrEmpty(props.DomainName)
                    ? props.HostName.ToLowerInvariant()
                    : (props.HostName + "." + props.DomainName).ToLowerInvariant();
            }
        }

        /// <summary>IPv4-Adressen aktiver Netzwerkkarten; Karten mit Gateway zuerst (meist LAN oder VPN).</summary>
        public static string IpAddresses
        {
            get
            {
                try
                {
                    var addresses = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .Select(n => new { Props = n.GetIPProperties() })
                        .OrderByDescending(n => n.Props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any)))
                        .SelectMany(n => n.Props.UnicastAddresses)
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) &&
                                    !a.Address.ToString().StartsWith("169.254."))
                        .Select(a => a.Address.ToString())
                        .Distinct()
                        .ToList();
                    return string.Join(", ", addresses);
                }
                catch (Exception ex)
                {
                    Log.Warn("IP-Adressen nicht ermittelbar: " + ex.Message);
                    return "";
                }
            }
        }

        public static DateTime BootTime => DateTime.Now - TimeSpan.FromMilliseconds(GetTickCount64());

        /// <summary>z. B. "Windows 11 24H2 (26100.4061)"</summary>
        public static string OsVersion
        {
            get
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return Environment.OSVersion.VersionString;
                    string build = key.GetValue("CurrentBuild") as string ?? "";
                    string ubr = key.GetValue("UBR")?.ToString() ?? "";
                    string display = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string ?? "";
                    int.TryParse(build, out int buildNumber);
                    string product = buildNumber >= 22000 ? "Windows 11" : "Windows 10";
                    return (product + " " + display + " (" + build + (ubr.Length > 0 ? "." + ubr : "") + ")").Trim();
                }
            }
        }

        /// <summary>Zuletzt an diesem PC angemeldeter Benutzer (Anmeldebildschirm).</summary>
        public static string LastLoggedOnUser
        {
            get
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI"))
                {
                    return key?.GetValue("LastLoggedOnSAMUser") as string ?? key?.GetValue("LastLoggedOnUser") as string ?? "";
                }
            }
        }

        public static string AgentVersion => typeof(SystemInfo).Assembly.GetName().Version.ToString(3);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
