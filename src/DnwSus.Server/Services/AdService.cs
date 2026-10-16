using System.DirectoryServices;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Options;

namespace DnwSus.Server.Services;

/// <summary>Ein Computerkonto aus dem AD (nur echte Clients: aktiviert, Client-Betriebssystem).</summary>
public sealed class ClientInfo
{
    public string Name { get; init; } = "";
    public string DnsName { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string DistinguishedName { get; init; } = "";

    /// <summary>lastLogonTimestamp aus dem AD (wird vom AD nur alle ~14 Tage aktualisiert).</summary>
    public DateTime? LastAdLogon { get; init; }

    /// <summary>Nummern der Update-Gruppen (1..3), in denen der Client Mitglied ist.</summary>
    public List<int> Groups { get; } = new();

    public bool IsStale { get; set; }
}

public sealed class GroupInfo
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public bool Found { get; set; }

    /// <summary>Mitglieder, die keine echten Clients sind (Server, deaktiviert …) und daher nicht angezeigt werden.</summary>
    public int HiddenMembers { get; set; }
}

public sealed class AdSnapshot
{
    public DateTime LoadedAt { get; init; } = DateTime.Now;
    public string Domain { get; init; } = "";
    public List<ClientInfo> Clients { get; init; } = new();
    public List<GroupInfo> Groups { get; init; } = new();
    public string? Error { get; init; }
}

/// <summary>Liest Update-Gruppen und Clients aus dem Active Directory.</summary>
public sealed class AdService
{
    // Echte PCs: Computerkonto mit Betriebssystem-Eintrag, kein Server, nicht deaktiviert.
    // Ohne operatingSystem fallen technische Konten (Cluster-Namen, AZUREADSSOACC …) heraus.
    private const string ClientFilter =
        "(&(objectCategory=computer)(operatingSystem=*)(!(operatingSystem=*Server*))" +
        "(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";

    private const string InChainRule = "1.2.840.113556.1.4.1941"; // verschachtelte Mitgliedschaft

    private readonly DnwSusOptions _options;
    private readonly ILogger<AdService> _logger;
    private readonly object _lock = new();
    private AdSnapshot? _snapshot;
    private SecurityIdentifier? _adminGroupSid;

    public AdService(IOptions<DnwSusOptions> options, ILogger<AdService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public AdSnapshot GetSnapshot(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _snapshot != null &&
                DateTime.Now - _snapshot.LoadedAt < TimeSpan.FromMinutes(_options.AdCacheMinutes))
                return _snapshot;

            try
            {
                _snapshot = Load();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AD-Abfrage fehlgeschlagen");
                _snapshot = new AdSnapshot
                {
                    Error = "AD-Abfrage fehlgeschlagen: " + ex.Message,
                    Groups = _options.Groups.Select((g, i) => new GroupInfo { Number = i + 1, Name = g }).ToList()
                };
            }
            return _snapshot;
        }
    }

    /// <summary>Darf dieser Benutzer das Dashboard nutzen? (Domänen-Admins bzw. eingestellte Gruppe)</summary>
    public bool IsAdmin(WindowsIdentity identity)
    {
        return IsAdmin(identity, out _);
    }

    /// <summary>
    /// Wie <see cref="IsAdmin(WindowsIdentity)"/>, liefert bei Ablehnung einen Grund für die Anzeige.
    /// Ruft ein Admin das Dashboard über localhost aus einem nicht erhöhten Browser auf, bekommt der Dienst
    /// wegen der Benutzerkontensteuerung das gefilterte Token: Admin-Gruppen sind darin nur „deny-only“
    /// und fehlen in <see cref="WindowsIdentity.Groups"/>. Für unsere Prüfung zählt die Mitgliedschaft,
    /// deshalb werden auch diese Einträge berücksichtigt.
    /// </summary>
    public bool IsAdmin(WindowsIdentity identity, out string? problem)
    {
        problem = null;
        var sid = GetAdminGroupSid();
        if (sid == null)
        {
            problem = "Die Admin-Gruppe konnte im AD nicht ermittelt werden (Details in der Ereignisanzeige, Quelle DnwSus.Server).";
            return false;
        }

        if (identity.Groups != null && identity.Groups.Contains(sid)) return true;
        if (identity.Claims.Any(c => c.Type == ClaimTypes.DenyOnlySid && c.Value == sid.Value)) return true;

        _logger.LogWarning("Dashboard-Zugriff verweigert für {User}: nicht Mitglied von {Group} ({Sid})",
            identity.Name, AdminGroupDisplayName, sid.Value);
        return false;
    }

    public string AdminGroupDisplayName =>
        string.IsNullOrWhiteSpace(_options.AdminGroup) ? "Domänen-Admins" : _options.AdminGroup;

    private SecurityIdentifier? GetAdminGroupSid()
    {
        lock (_lock)
        {
            if (_adminGroupSid != null) return _adminGroupSid;
            try
            {
                using var root = new DirectoryEntry();
                if (string.IsNullOrWhiteSpace(_options.AdminGroup))
                {
                    var domainSid = new SecurityIdentifier((byte[])root.Properties["objectSid"].Value!, 0);
                    _adminGroupSid = new SecurityIdentifier(WellKnownSidType.AccountDomainAdminsSid, domainSid);
                }
                else
                {
                    using var searcher = new DirectorySearcher(root,
                        "(&(objectCategory=group)(sAMAccountName=" + Escape(_options.AdminGroup) + "))",
                        new[] { "objectSid" });
                    var result = searcher.FindOne();
                    if (result != null)
                        _adminGroupSid = new SecurityIdentifier((byte[])result.Properties["objectSid"][0], 0);
                    else
                        _logger.LogError("Admin-Gruppe {Group} nicht im AD gefunden", _options.AdminGroup);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin-Gruppe konnte nicht ermittelt werden");
            }
            return _adminGroupSid;
        }
    }

    private AdSnapshot Load()
    {
        using var root = new DirectoryEntry();
        string domain = DnToDomain(root.Properties["distinguishedName"].Value as string ?? "");

        // 1. Alle echten Clients
        var clients = new Dictionary<string, ClientInfo>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new DirectorySearcher(root, ClientFilter, new[]
               { "name", "dNSHostName", "operatingSystem", "operatingSystemVersion", "lastLogonTimestamp", "distinguishedName" }))
        {
            searcher.PageSize = 1000;
            using var results = searcher.FindAll();
            foreach (SearchResult sr in results)
            {
                string dn = Get(sr, "distinguishedName");
                string name = Get(sr, "name");
                if (dn.Length == 0 || name.Length == 0) continue;

                DateTime? lastLogon = null;
                if (sr.Properties.Contains("lastLogonTimestamp") && sr.Properties["lastLogonTimestamp"][0] is long ft && ft > 0)
                    lastLogon = DateTime.FromFileTimeUtc(ft).ToLocalTime();

                clients[dn] = new ClientInfo
                {
                    Name = name.ToUpperInvariant(),
                    DnsName = Get(sr, "dNSHostName").ToLowerInvariant(),
                    OperatingSystem = Get(sr, "operatingSystem"),
                    OsVersion = Get(sr, "operatingSystemVersion"),
                    DistinguishedName = dn,
                    LastAdLogon = lastLogon,
                    IsStale = lastLogon == null || lastLogon < DateTime.Now.AddDays(-_options.StaleDays)
                };
            }
        }

        // 2. Mitglieder der Update-Gruppen (auch verschachtelt)
        var groups = new List<GroupInfo>();
        for (int i = 0; i < _options.Groups.Count; i++)
        {
            var group = new GroupInfo { Number = i + 1, Name = _options.Groups[i] };
            groups.Add(group);

            string? groupDn = FindGroupDn(root, group.Name);
            if (groupDn == null) continue;
            group.Found = true;

            using var searcher = new DirectorySearcher(root,
                "(&(objectCategory=computer)(memberOf:" + InChainRule + ":=" + Escape(groupDn) + "))",
                new[] { "distinguishedName" });
            searcher.PageSize = 1000;
            using var results = searcher.FindAll();
            foreach (SearchResult sr in results)
            {
                if (clients.TryGetValue(Get(sr, "distinguishedName"), out var client))
                    client.Groups.Add(group.Number);
                else
                    group.HiddenMembers++;
            }
        }

        return new AdSnapshot
        {
            Domain = domain,
            Clients = clients.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Groups = groups
        };
    }

    private static string? FindGroupDn(DirectoryEntry root, string samAccountName)
    {
        using var searcher = new DirectorySearcher(root,
            "(&(objectCategory=group)(sAMAccountName=" + Escape(samAccountName) + "))",
            new[] { "distinguishedName" });
        var result = searcher.FindOne();
        return result == null ? null : Get(result, "distinguishedName");
    }

    private static string Get(SearchResult sr, string property) =>
        sr.Properties.Contains(property) && sr.Properties[property].Count > 0
            ? sr.Properties[property][0]?.ToString() ?? ""
            : "";

    private static string DnToDomain(string dn) =>
        string.Join(".", dn.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Substring(3)));

    /// <summary>Maskiert Sonderzeichen für LDAP-Filter (RFC 4515).</summary>
    private static string Escape(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
