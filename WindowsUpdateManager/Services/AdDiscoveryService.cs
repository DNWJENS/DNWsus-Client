using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Threading.Tasks;
using WindowsUpdateManager.Models;

namespace WindowsUpdateManager.Services
{
    public class AdDiscoveryService
    {
        /// <summary>
        /// Retrieves all Windows Server computers from Active Directory.
        /// </summary>
        /// <param name="ldapFilter">Optional OU distinguishedName to use as LDAP root (e.g. "OU=Servers,DC=example,DC=com").</param>
        public async Task<List<ServerInfo>> GetServersAsync(string ldapFilter = null)
        {
            return await Task.Run(() =>
            {
                var results = new List<ServerInfo>();

                DirectoryEntry root;
                if (!string.IsNullOrWhiteSpace(ldapFilter))
                    root = new DirectoryEntry("LDAP://" + ldapFilter);
                else
                    root = new DirectoryEntry();

                using (root)
                using (var searcher = new DirectorySearcher(root))
                {
                    searcher.Filter = "(&(objectCategory=computer)(operatingSystem=*Server*))";
                    searcher.PropertiesToLoad.Add("name");
                    searcher.PropertiesToLoad.Add("operatingSystem");
                    searcher.PropertiesToLoad.Add("distinguishedName");
                    searcher.PageSize = 1000;

                    using (var searchResults = searcher.FindAll())
                    {
                        foreach (SearchResult sr in searchResults)
                        {
                            string name = GetProperty(sr, "name");
                            string os   = GetProperty(sr, "operatingSystem");
                            string dn   = GetProperty(sr, "distinguishedName");
                            string ou   = ExtractFirstOu(dn);

                            if (string.IsNullOrEmpty(name)) continue;

                            results.Add(new ServerInfo
                            {
                                Name            = name.ToUpperInvariant(),
                                OperatingSystem = os ?? "",
                                OU              = ou ?? "",
                                Status          = ServerStatus.Unknown
                            });
                        }
                    }
                }

                results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return results;
            });
        }

        /// <summary>
        /// Retrieves all OUs from Active Directory.
        /// </summary>
        public async Task<List<string>> GetOUsAsync()
        {
            return await Task.Run(() =>
            {
                var results = new List<string>();

                using (var root = new DirectoryEntry())
                using (var searcher = new DirectorySearcher(root))
                {
                    searcher.Filter = "(objectCategory=organizationalUnit)";
                    searcher.PropertiesToLoad.Add("name");
                    searcher.PropertiesToLoad.Add("distinguishedName");
                    searcher.PageSize = 1000;

                    using (var searchResults = searcher.FindAll())
                    {
                        foreach (SearchResult sr in searchResults)
                        {
                            string dn = GetProperty(sr, "distinguishedName");
                            if (!string.IsNullOrEmpty(dn))
                                results.Add(dn);
                        }
                    }
                }

                results.Sort(StringComparer.OrdinalIgnoreCase);
                return results;
            });
        }

        // ------------------------------------------------------------------

        private static string GetProperty(SearchResult sr, string propertyName)
        {
            if (sr.Properties.Contains(propertyName) && sr.Properties[propertyName].Count > 0)
            {
                object val = sr.Properties[propertyName][0];
                return val != null ? val.ToString() : null;
            }
            return null;
        }

        /// <summary>
        /// Extracts the first "OU=..." component from a distinguishedName.
        /// E.g. "CN=SRV01,OU=Servers,OU=IT,DC=corp,DC=com" → "OU=Servers,OU=IT,DC=corp,DC=com"
        /// Returns the first OU= value only (e.g. "Servers").
        /// </summary>
        private static string ExtractFirstOu(string distinguishedName)
        {
            if (string.IsNullOrEmpty(distinguishedName)) return "";

            var parts = distinguishedName.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(3);
            }

            return "";
        }
    }
}
