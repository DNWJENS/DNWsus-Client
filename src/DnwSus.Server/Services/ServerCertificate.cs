using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DnwSus.Server.Services;

/// <summary>
/// Das HTTPS-Zertifikat des Servers. Es wird beim ersten Start selbst ausgestellt
/// (10 Jahre gültig) und im Zertifikatsspeicher des Computers abgelegt. Die Agenten
/// vertrauen genau diesem Zertifikat über den Fingerabdruck, der in ihre MSI eingebaut wird.
/// </summary>
public static class ServerCertificate
{
    public const string FriendlyName = "DNWsus Server";

    /// <summary>
    /// Lädt das Zertifikat aus LocalMachine\My bzw. legt es dort an. Fehlen dafür die
    /// Rechte (Testlauf ohne Admin), wird auf CurrentUser\My ausgewichen.
    /// </summary>
    public static X509Certificate2 LoadOrCreate(out string storeInfo)
    {
        try
        {
            var cert = LoadOrCreate(StoreLocation.LocalMachine);
            storeInfo = "LocalMachine\\My";
            return cert;
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException)
        {
            // Kein Admin: nur für Entwicklung/Test. Der Dienst läuft als SYSTEM und kommt hier nie hin.
            var cert = LoadOrCreate(StoreLocation.CurrentUser);
            storeInfo = "CurrentUser\\My (kein Admin – nur für Tests)";
            return cert;
        }
    }

    private static X509Certificate2 LoadOrCreate(StoreLocation location)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Where(c => c.FriendlyName == FriendlyName && c.HasPrivateKey && c.NotAfter > DateTime.Now)
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();
        if (existing != null)
            return existing;

        var cert = Create(location);
        store.Add(cert);

        // Auf dem Server selbst als vertrauenswürdig eintragen, damit der Browser dort nicht warnt.
        // Für CurrentUser würde Windows dafür einen Bestätigungsdialog zeigen, daher nur bei LocalMachine.
        if (location == StoreLocation.LocalMachine)
        {
            using var root = new X509Store(StoreName.Root, location);
            root.Open(OpenFlags.ReadWrite);
            root.Add(X509CertificateLoader.LoadCertificate(cert.RawData));
        }

        return cert;
    }

    private static X509Certificate2 Create(StoreLocation location)
    {
        string host = Environment.MachineName.ToLowerInvariant();
        string fqdn = GetFqdn();

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=" + fqdn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(fqdn);
        if (!string.Equals(fqdn, host, StringComparison.OrdinalIgnoreCase))
            san.AddDnsName(host);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Serverauthentifizierung

        var now = DateTimeOffset.Now;
        using var ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        // Über PFX neu laden, damit der private Schlüssel dauerhaft im Speicher bleibt.
        var flags = X509KeyStorageFlags.PersistKeySet |
                    (location == StoreLocation.LocalMachine ? X509KeyStorageFlags.MachineKeySet : X509KeyStorageFlags.UserKeySet);
        var cert = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null, flags);
        cert.FriendlyName = FriendlyName;
        return cert;
    }

    public static string GetFqdn()
    {
        var props = IPGlobalProperties.GetIPGlobalProperties();
        string host = props.HostName.ToLowerInvariant();
        string domain = props.DomainName.ToLowerInvariant();
        return string.IsNullOrEmpty(domain) ? host : host + "." + domain;
    }
}
