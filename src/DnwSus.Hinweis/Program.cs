using System;
using System.Globalization;
using System.Windows.Forms;

namespace DnwSus.Hinweis
{
    /// <summary>
    /// Hinweisfenster zum Neustart nach Windows-Updates. Wird vom DNWsus-Agenten in der Sitzung des
    /// angemeldeten Benutzers gestartet; die Antwort des Benutzers geht als Exit-Code zurück:
    ///   0          = geschlossen / später erinnern
    ///   1          = jetzt neu starten
    ///   1000 + n   = in n Minuten neu starten
    /// Aufruf:
    ///   DnwSus.Hinweis.exe frage1                       Gruppe 1: jetzt oder Zeitpunkt wählen (max. 24 h)
    ///   DnwSus.Hinweis.exe frage2 yyyy-MM-ddTHH:mm:ss   Gruppe 2: jetzt / 15 / 60 / 120 Min., Frist = Zeitpunkt
    ///   DnwSus.Hinweis.exe countdown yyyy-MM-ddTHH:mm:ss Neustart steht fest, Countdown bis zum Zeitpunkt
    /// </summary>
    internal static class Program
    {
        public const int RebootNow = 1;
        public const int DelayBase = 1000;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            DateTime? time = args.Length > 1 &&
                DateTime.TryParseExact(args[1], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : (DateTime?)null;

            HintForm form;
            switch (mode)
            {
                case "frage1":
                    form = HintForm.AskGroup1();
                    break;
                case "frage2" when time.HasValue:
                    form = HintForm.AskGroup2(time.Value);
                    break;
                case "countdown" when time.HasValue:
                    form = HintForm.Countdown(time.Value);
                    break;
                default:
                    return 0;
            }

            Application.Run(form);
            return form.Result;
        }
    }
}
