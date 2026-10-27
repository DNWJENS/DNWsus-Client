using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace DnwSus.Server.Services;

/// <summary>
/// Kopiert die aktuelle, an diesen Server gebundene Agent-MSI an einen von den Clients erreichbaren Zielpfad
/// (fester Dateiname, wird überschrieben). Zwei Wege, beide ohne gespeichertes Passwort:
///  - ohne Credentials: der Server schreibt mit seiner eigenen Identität (für eine eigene Freigabe, auf der das
///    Computerkonto Schreibrecht hat – vollautomatisch möglich),
///  - mit Credentials: einmalige Anmeldung nur für diese Kopie (z. B. für NETLOGON/SYSVOL, wo SYSTEM nicht schreiben darf).
/// Die Zugangsdaten werden nie gespeichert.
/// </summary>
public sealed class AgentDistribution
{
    private const int Logon32LogonNewCredentials = 9; // nur für ausgehende Netzwerkverbindungen, kein Sonderrecht nötig
    private const int Logon32ProviderWinnt50 = 3;

    private readonly AgentMsiService _msi;

    public AgentDistribution(AgentMsiService msi) => _msi = msi;

    /// <summary>Quelle (lokale, gebundene MSI) – für die manuelle Kopier-Anweisung, falls das Schreiben scheitert.</summary>
    public string SourceMsiPath
    {
        get { try { return _msi.TemplateExists ? _msi.GetMsiPath() : ""; } catch { return ""; } }
    }

    public (bool Ok, string Message) CopyToTarget(string? targetPath, string? user, string? password)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return (false, "Kein Zielpfad angegeben.");

        string source;
        try { source = _msi.GetMsiPath(); }
        catch (Exception ex) { return (false, "Agent-MSI konnte nicht erzeugt werden: " + ex.Message); }

        try
        {
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(password))
            {
                SplitUser(user, out string domain, out string name);
                if (!LogonUser(name, domain, password, Logon32LogonNewCredentials, Logon32ProviderWinnt50, out var token))
                    return (false, "Anmeldung fehlgeschlagen (Windows-Fehler " + Marshal.GetLastWin32Error() + ").");
                using (token)
                    WindowsIdentity.RunImpersonated(token, () => File.Copy(source, targetPath, overwrite: true));
            }
            else
            {
                File.Copy(source, targetPath, overwrite: true);
            }
            return (true, "Agent-MSI nach " + targetPath + " kopiert.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Kein Schreibrecht auf dem Zielpfad.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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
