using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Script.Serialization;

namespace DnwSus.Agent
{
    /// <summary>
    /// Verbindung zum DNWsus-Server. Vertraut ausschließlich dem Zertifikat, dessen SHA-256-Fingerabdruck
    /// die MSI in die Registry geschrieben hat. Anmeldung per Kerberos mit dem Computerkonto (Dienst läuft als SYSTEM).
    /// </summary>
    internal sealed class ServerClient
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static ServerClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public AgentResponse Report(AgentReport report)
        {
            string url = AgentPaths.ServerUrl;
            string pinned = AgentPaths.CertificateHash.Replace(" ", "").ToUpperInvariant();
            if (url.Length == 0 || pinned.Length == 0)
                throw new InvalidOperationException("Server-Adresse oder Zertifikat fehlen in der Registry (HKLM\\" + AgentPaths.ConfigKey + ").");

            var handler = new WebRequestHandler
            {
                UseDefaultCredentials = true,
                PreAuthenticate = true,
                ServerCertificateValidationCallback = (sender, certificate, chain, errors) => Matches(certificate, pinned)
            };

            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
            using (var content = new StringContent(_json.Serialize(report), Encoding.UTF8, "application/json"))
            {
                var response = client.PostAsync(new Uri(new Uri(url), "api/agent/report"), content).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Server antwortet mit " + (int)response.StatusCode + ": " + Shorten(body));
                return _json.Deserialize<AgentResponse>(body);
            }
        }

        private static bool Matches(X509Certificate certificate, string pinnedHash)
        {
            if (certificate == null) return false;
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(certificate.GetRawCertData())).Replace("-", "");
                bool ok = string.Equals(hash, pinnedHash, StringComparison.OrdinalIgnoreCase);
                if (!ok) Log.Warn("Serverzertifikat abgelehnt: Fingerabdruck " + hash + " passt nicht zur MSI.");
                return ok;
            }
        }

        private static string Shorten(string text) =>
            text == null ? "" : text.Length > 200 ? text.Substring(0, 200) + "…" : text;
    }
}
