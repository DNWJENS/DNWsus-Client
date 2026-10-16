using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DnwSus.Server.Data;

namespace DnwSus.Server.Services;

/// <summary>
/// Erzeugt die Agent-MSI für diesen Server: Die neutrale Vorlage (agent\DnwSus-Agent.msi, wird mit dem
/// Server ausgeliefert) wird kopiert und Server-URL sowie Zertifikats-Fingerabdruck werden in die
/// Property-Tabelle eingetragen. Dafür wird die Windows-Installer-Schnittstelle (msi.dll) genutzt.
/// </summary>
public sealed class AgentMsiService
{
    private readonly ServerIdentity _identity;
    private readonly Database _db;
    private readonly object _lock = new();

    public AgentMsiService(ServerIdentity identity, Database db)
    {
        _identity = identity;
        _db = db;
    }

    public string TemplatePath => Path.Combine(AppContext.BaseDirectory, "agent", "DnwSus-Agent.msi");

    public bool TemplateExists => File.Exists(TemplatePath);

    /// <summary>Pfad der fertigen MSI; wird bei Bedarf (neue Vorlage, anderes Zertifikat) neu erzeugt.</summary>
    public string GetMsiPath()
    {
        if (!TemplateExists)
            throw new FileNotFoundException("Agent-Vorlage fehlt im Installationsordner des Servers.", TemplatePath);

        lock (_lock)
        {
            var template = new FileInfo(TemplatePath);
            string key = Hash(_identity.Url + "|" + _identity.CertificateHash + "|" + template.Length + "|" + template.LastWriteTimeUtc.Ticks);
            string dir = Path.Combine(_db.DataDirectory, "msi");
            string target = Path.Combine(dir, $"DnwSus-Agent-{key[..12]}.msi");
            if (File.Exists(target)) return target;

            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "DnwSus-Agent-*.msi")) File.Delete(old);

            string temp = target + ".tmp";
            File.Copy(TemplatePath, temp, overwrite: true);
            try
            {
                Patch(temp, new Dictionary<string, string>
                {
                    ["DNWSUS_SERVERURL"] = _identity.Url,
                    ["DNWSUS_CERTHASH"] = _identity.CertificateHash
                }, PackageCodeFrom(key));
                File.Move(temp, target);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return target;
        }
    }

    private static void Patch(string msiPath, Dictionary<string, string> properties, Guid packageCode)
    {
        Check(MsiOpenDatabase(msiPath, MsiDbOpenTransact, out var db), "MsiOpenDatabase");
        try
        {
            foreach (var (name, value) in properties)
            {
                if (value.Contains('\'') || name.Contains('\''))
                    throw new InvalidOperationException("Ungültiges Zeichen in MSI-Eigenschaft " + name);
                Check(MsiDatabaseOpenView(db, $"UPDATE `Property` SET `Value`='{value}' WHERE `Property`='{name}'", out var view),
                    "MsiDatabaseOpenView");
                try { Check(MsiViewExecute(view, IntPtr.Zero), "MsiViewExecute"); }
                finally { MsiCloseHandle(view); }
            }

            // Eigener Paketcode, damit Windows Installer die angepasste MSI nicht mit der Vorlage verwechselt
            Check(MsiGetSummaryInformation(db, null, 1, out var summary), "MsiGetSummaryInformation");
            try
            {
                Check(MsiSummaryInfoSetProperty(summary, PidRevNumber, VtLpstr, 0, IntPtr.Zero,
                    packageCode.ToString("B").ToUpperInvariant()), "MsiSummaryInfoSetProperty");
                Check(MsiSummaryInfoPersist(summary), "MsiSummaryInfoPersist");
            }
            finally { MsiCloseHandle(summary); }

            Check(MsiDatabaseCommit(db), "MsiDatabaseCommit");
        }
        finally
        {
            MsiCloseHandle(db);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Guid PackageCodeFrom(string key) => new(Convert.FromHexString(key[..32]));

    private static void Check(uint result, string function)
    {
        if (result != 0) throw new InvalidOperationException($"{function} fehlgeschlagen (Fehler {result}).");
    }

    // ---- msi.dll ----
    private static readonly IntPtr MsiDbOpenTransact = new(1);
    private const uint PidRevNumber = 9;
    private const uint VtLpstr = 30;

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr handle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiDatabaseCommit(IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetSummaryInformationW")]
    private static extern uint MsiGetSummaryInformation(IntPtr database, string? path, uint updateCount, out IntPtr summary);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiSummaryInfoSetPropertyW")]
    private static extern uint MsiSummaryInfoSetProperty(IntPtr summary, uint property, uint dataType, int intValue,
        IntPtr fileTime, string value);

    [DllImport("msi.dll")]
    private static extern uint MsiSummaryInfoPersist(IntPtr summary);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
