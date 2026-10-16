using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DnwSus.Server.Services;

/// <summary>Adresse und Zertifikat des Servers, so wie sie in die Agent-MSI eingebaut werden.</summary>
public sealed class ServerIdentity
{
    public ServerIdentity(X509Certificate2 certificate, int port)
    {
        Certificate = certificate;
        Url = $"https://{ServerCertificate.GetFqdn()}:{port}/";
        CertificateHash = Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    public X509Certificate2 Certificate { get; }

    /// <summary>z. B. https://srv01.firma.local:13162/</summary>
    public string Url { get; }

    /// <summary>SHA-256 über das Zertifikat (Hex, Großbuchstaben). Der Agent vertraut nur diesem Zertifikat.</summary>
    public string CertificateHash { get; }
}
