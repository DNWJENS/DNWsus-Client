using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DnwSus.Server.Services;

/// <summary>
/// Legt eine SMB-Freigabe an bzw. hält sie aktuell (netapi32.dll). Freigabe-Leserecht für „Jeder" (öffentlich) –
/// die eigentliche Zugriffsbeschränkung macht ohnehin das NTFS-Recht des Ordners. Der Dienst läuft als SYSTEM
/// (lokaler Administrator) und darf das. Firewall wird NICHT angefasst.
/// </summary>
public static class WindowsShare
{
    private const uint StypeDisktree = 0;
    private const int Level502 = 502;
    private const int NerrSuccess = 0;
    private const int NerrDuplicateShare = 2118;

    private const int FileGenericRead = 0x1200A9;
    private const int FileAllAccess = 0x1F01FF;

    /// <summary>Erstellt die Freigabe oder korrigiert eine bestehende gleichen Namens. Wirft bei Fehlern.</summary>
    public static void Ensure(string shareName, string folderPath, string remark)
    {
        byte[] sd = BuildSecurityDescriptor();
        IntPtr sdPtr = Marshal.AllocHGlobal(sd.Length);
        try
        {
            Marshal.Copy(sd, 0, sdPtr, sd.Length);
            var info = new ShareInfo502
            {
                netname = shareName,
                type = StypeDisktree,
                remark = remark,
                permissions = 0,
                max_uses = uint.MaxValue,
                current_uses = 0,
                path = folderPath,
                passwd = null,
                reserved = 0,
                security_descriptor = sdPtr
            };

            int result = NetShareAdd(null, Level502, ref info, out int paramErr);
            if (result == NerrDuplicateShare)
            {
                // Freigabe existiert bereits → Pfad und Rechte auf unseren Stand bringen.
                result = NetShareSetInfo(null, shareName, Level502, ref info, out paramErr);
            }
            if (result != NerrSuccess)
                throw new InvalidOperationException($"Freigabe „{shareName}“ konnte nicht angelegt werden (netapi32-Fehler {result}, Feld {paramErr}).");
        }
        finally
        {
            Marshal.FreeHGlobal(sdPtr);
        }
    }

    /// <summary>Selbst-relativer Security-Descriptor: „Jeder" lesen, Administratoren Vollzugriff (nur Freigabe-Ebene).</summary>
    private static byte[] BuildSecurityDescriptor()
    {
        var dacl = new DiscretionaryAcl(isContainer: false, isDS: false, capacity: 2);
        dacl.AddAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileGenericRead, InheritanceFlags.None, PropagationFlags.None);
        dacl.AddAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileAllAccess, InheritanceFlags.None, PropagationFlags.None);

        var sd = new CommonSecurityDescriptor(isContainer: false, isDS: false,
            ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative,
            owner: null, group: null, systemAcl: null, discretionaryAcl: dacl);

        byte[] bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo502
    {
        public string netname;
        public uint type;
        public string? remark;
        public uint permissions;
        public uint max_uses;
        public uint current_uses;
        public string path;
        public string? passwd;
        public uint reserved;
        public IntPtr security_descriptor;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareAdd(string? servername, int level, ref ShareInfo502 buf, out int parm_err);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareSetInfo(string? servername, string netname, int level, ref ShareInfo502 buf, out int parm_err);
}
