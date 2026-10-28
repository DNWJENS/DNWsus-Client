namespace DnwSus.Server.Services;

/// <summary>
/// Update-Klassen und die Standard-Regeln je Klasse. Eine Regel sagt, <b>wann</b> installiert wird (Install) und
/// <b>wie</b> der Neustart gehandhabt wird (Reboot). Pro Gruppe können einzelne Klassen abweichend gespeichert werden
/// (Tabelle policies); fehlt ein Eintrag, gilt der Standard hier. Gegenstück im Agenten: UpdateClassifier.
/// </summary>
public static class UpdatePolicy
{
    // Klassen – Namen müssen mit UpdateClassifier (Agent) übereinstimmen.
    public const string Definition = "Definition";
    public const string Security = "Security";
    public const string Quality = "Quality";
    public const string Feature = "Feature";
    public const string Driver = "Driver";
    public const string Product = "Product";
    public const string Other = "Other";

    public static readonly string[] Classes =
        { Definition, Security, Quality, Feature, Driver, Product, Other };

    // Install-Modi
    public const string Immediate = "immediate"; // Agent installiert selbst (beim Scan)
    public const string Schedule = "schedule";   // nur per Zeitplan-Lauf
    public const string Manual = "manual";       // nur per Dashboard-Lauf
    public const string Off = "off";             // nie

    // Reboot-Modi
    public const string RebootTemplate = "template"; // wie Gruppe (deren Standardverhalten)
    public const string RebootPrompt = "prompt";     // immer mit Zustimmung (Popup)
    public const string RebootAuto = "auto";         // automatisch nach kurzem Hinweis
    public const string RebootNone = "none";         // kein erzwungener Neustart

    public sealed record Rule(string Install, string Reboot);

    /// <summary>Vernünftige Standardregeln – greifen, solange nichts Abweichendes gespeichert ist.</summary>
    public static Rule Default(string cls) => cls switch
    {
        Definition => new Rule(Immediate, RebootTemplate), // kein Neustart nötig
        Security   => new Rule(Immediate, RebootTemplate),
        Quality    => new Rule(Schedule,  RebootTemplate),
        Feature    => new Rule(Off,       RebootTemplate),
        Driver     => new Rule(Off,       RebootTemplate),
        Product    => new Rule(Manual,    RebootTemplate), // könnte Dienste stoppen
        _          => new Rule(Manual,    RebootTemplate),
    };

    public static bool IsValidInstall(string v) => v is Immediate or Schedule or Manual or Off;
    public static bool IsValidReboot(string v) => v is RebootTemplate or RebootPrompt or RebootAuto or RebootNone;

    public static string Label(string cls) => cls switch
    {
        Definition => "Definition",
        Security   => "Sicherheit & kritisch",
        Quality    => "Qualität / Patchday",
        Feature    => "Funktion / Upgrade",
        Driver     => "Treiber",
        Product    => "Produkt (SQL/Office)",
        _          => "Sonstige",
    };
}
