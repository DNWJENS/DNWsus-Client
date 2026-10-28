using System;
using System.Collections.Generic;

namespace DnwSus.Agent
{
    /// <summary>
    /// Ordnet ein Update anhand seiner Windows-Update-Kategorien einer von wenigen Klassen zu. Gleiche Klassen-Namen
    /// verwendet der Server (Regeln pro Gruppe × Klasse). Gegenstück der Namen: UpdatePolicy im Server.
    /// </summary>
    internal static class UpdateClassifier
    {
        public const string Definition = "Definition";
        public const string Security = "Security";
        public const string Quality = "Quality";
        public const string Feature = "Feature";
        public const string Driver = "Driver";
        public const string Product = "Product";
        public const string Other = "Other";

        /// <summary>Bestimmt die Klasse aus allen Kategorienamen eines Updates (Reihenfolge = Priorität).</summary>
        public static string Classify(IEnumerable<string> categories)
        {
            var cats = new List<string>();
            if (categories != null)
                foreach (var c in categories)
                    if (!string.IsNullOrEmpty(c)) cats.Add(c.ToLowerInvariant());

            if (Any(cats, "definition")) return Definition;
            if (Any(cats, "security", "critical")) return Security;
            if (Any(cats, "driver")) return Driver;
            if (Any(cats, "feature pack", "service pack", "upgrade")) return Feature;
            if (Any(cats, "sql", "office", "exchange", "sharepoint", ".net", "visual studio", "microsoft server")) return Product;
            if (Any(cats, "update rollup", "updates")) return Quality;
            return Other;
        }

        private static bool Any(List<string> cats, params string[] needles)
        {
            foreach (var c in cats)
                foreach (var n in needles)
                    if (c.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
