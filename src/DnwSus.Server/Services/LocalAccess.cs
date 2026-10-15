using System.Net;
using System.Net.NetworkInformation;

namespace DnwSus.Server.Services;

/// <summary>
/// Entscheidet, ob eine Anfrage vom Server selbst kommt. Das Dashboard ist nur dort erreichbar.
/// Ruft man auf dem Server die eigene FQDN auf, kommt die Anfrage nicht von 127.0.0.1, sondern
/// von einer eigenen Netzwerkadresse – die zählt deshalb ebenfalls als lokal.
/// </summary>
public static class LocalAccess
{
    /// <summary>Pfade, die auch von Clients erreichbar sind (Agent-Schnittstelle, MSI-Download).</summary>
    private static readonly string[] RemotePaths = { "/api/agent", "/download" };

    public static bool IsRemoteAllowedPath(PathString path) =>
        RemotePaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsLocal(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote)) return true;

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.Equals(remote));
    }
}
