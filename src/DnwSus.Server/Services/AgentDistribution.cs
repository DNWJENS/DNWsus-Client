using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace DnwSus.Server.Services;

/// <summary>
/// Legt die aktuelle, an diesen Server gebundene Agent-MSI an einen von den Clients erreichbaren Zielpfad
/// (fester Dateiname <see cref="FileName"/>, wird überschrieben), damit die GPO-Aufgabe nie angepasst werden muss.
/// Zwei Wege, beide ohne gespeichertes Passwort:
///  - ohne Zugangsdaten: der Server schreibt mit seiner eigenen Identität (SYSTEM) – für einen lokalen Pfad oder eine
///    Freigabe, auf der das Computerkonto Schreibrecht hat; so läuft auch die automatische Veröffentlichung (siehe
///    <see cref="AgentPublishService"/>),
///  - mit Zugangsdaten: einmalige Anmeldung nur für diese Kopie (z. B. NETLOGON/SYSVOL, wo SYSTEM nicht schreiben darf).
/// Zugangsdaten werden nie gespeichert.
/// </summary>
public sealed class AgentDistribution
{
    /// <summary>Schlüssel des optionalen abweichenden Zielpfads in der settings-Tabelle (leer = Server-eigene Freigabe).</summary>
    public const string TargetSettingKey = "AgentTargetPath";

    /// <summary>Fester Dateiname am Ziel – passend zum GPO-Aufgabenpfad.</summary>
    public const string FileName = "DnwSus-Agent.msi";

    /// <summary>Name der Freigabe, die der Server selbst anlegt (öffentlich lesbar).</summary>
    public const string ShareName = "DNWsus";

    /// <summary>Lokaler Ordner hinter der Server-eigenen Freigabe (Standard-Ablage der Agent-MSI).</summary>
    public static string DefaultShareFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DNWsus", "Share");

    private const int Logon32LogonNewCredentials = 9; // nur für ausgehende Netzwerkverbindungen, kein Sonderrecht nötig
    private const int Logon32ProviderWinnt50 = 3;

    private readonly AgentMsiService _msi;

    public AgentDistribution(AgentMsiService msi) => _msi = msi;

    /// <summary>Quelle (lokale, gebundene MSI) – für die manuelle Kopier-Anweisung, falls das Schreiben scheitert.</summary>
    public string SourceMsiPath
    {
        get { try { return _msi.TemplateExists ? _msi.GetMsiPath() : ""; } catch { return ""; } }
    }

    /// <summary>Manuelle Kopie aus dem Dashboard (optional mit einmaligen Zugangsdaten).</summary>
    public (bool Ok, string Message) CopyToTarget(string? targetPath, string? user, string? password)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return (false, "Kein Zielpfad angegeben.");

        string source;
        try { source = _msi.GetMsiPath(); }
        catch (Exception ex) { return (false, "Agent-MSI konnte nicht erzeugt werden: " + ex.Message); }

        string target = NormalizeTarget(targetPath);
        try
        {
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(password))
            {
                SplitUser(user, out string domain, out string name);
                if (!LogonUser(name, domain, password, Logon32LogonNewCredentials, Logon32ProviderWinnt50, out var token))
                    return (false, "Anmeldung fehlgeschlagen (Windows-Fehler " + Marshal.GetLastWin32Error() + ").");
                using (token)
                    WindowsIdentity.RunImpersonated(token, () => CopyFile(source, target));
            }
            else
            {
                CopyFile(source, target);
            }
            return (true, "Agent-MSI nach „" + target + "“ kopiert.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Kein Schreibrecht auf „" + target + "“.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message + " (Ziel: „" + target + "“)");
        }
    }

    /// <summary>
    /// Automatische Veröffentlichung mit Server-Identität: kopiert nur, wenn am Ziel eine andere/ältere Datei liegt.
    /// Gibt zurück, ob kopiert wurde und eine Statusmeldung.
    /// </summary>
    public (bool Ok, string Message, bool Copied) PublishIfNewer(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return (true, "kein Zielpfad gesetzt", false);

        string source;
        try { source = _msi.GetMsiPath(); }
        catch (Exception ex) { return (false, ex.Message, false); }

        string target = NormalizeTarget(targetPath);
        try
        {
            if (File.Exists(target))
            {
                var s = new FileInfo(source);
                var t = new FileInfo(target);
                if (t.Length == s.Length && t.LastWriteTimeUtc >= s.LastWriteTimeUtc)
                    return (true, "aktuell", false);
            }
            CopyFile(source, target);
            return (true, "kopiert nach „" + target + "“", true);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, false);
        }
    }

    private static void CopyFile(string source, string target)
    {
        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>Ordner (endet auf \ oder ohne „.msi“) → fester Dateiname wird angehängt; sonst unverändert.</summary>
    public static string NormalizeTarget(string target)
    {
        target = target.Trim().Trim('"');
        bool looksLikeFolder = target.EndsWith("\\") || target.EndsWith("/")
            || !target.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        return looksLikeFolder ? target.TrimEnd('\\', '/') + "\\" + FileName : target;
    }

    /// <summary>"DOMÄNE\\Benutzer" → Domäne + Benutzer; "user@domain" bleibt als UPN; sonst lokale Domäne ".".</summary>
    private static void SplitUser(string user, out string domain, out string name)
    {
        int slash = user.IndexOf('\\');
        if (slash >= 0) { domain = user.Substring(0, slash); name = user.Substring(slash + 1); }
        else if (user.Contains('@')) { domain = ""; name = user; }
        else { domain = "."; name = user; }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string user, string domain, string password,
        int logonType, int logonProvider, out SafeAccessTokenHandle token);
}
