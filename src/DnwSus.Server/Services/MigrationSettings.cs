namespace DnwSus.Server.Services;

/// <summary>Settings-Schlüssel für den Serverumzug (Adresse + Zertifikat des neuen Servers).</summary>
public static class MigrationSettings
{
    public const string UrlKey = "MoveToUrl";
    public const string CertHashKey = "MoveToCertHash";
}
