using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DnwSus.Hinweis
{
    /// <summary>Helles Hinweisfenster im DNW-Design: Kopf mit Logo und „DNWsus" in der Hausschrift Ubuntu.</summary>
    internal sealed class HintForm : Form
    {
        private static readonly Color Brand = Color.FromArgb(0, 120, 212);
        private static readonly Color BrandDark = Color.FromArgb(0, 40, 110);
        private static readonly Color TextColor = Color.FromArgb(30, 30, 30);
        private static readonly Color Muted = Color.FromArgb(100, 100, 100);

        // Eingebettete Hausschrift (Ubuntu) und Logo – auf Clients ist Ubuntu nicht installiert, daher mitgeliefert.
        private static readonly PrivateFontCollection FontCollection = new PrivateFontCollection();
        private static readonly FontFamily BrandFamily = LoadBrandFamily();
        private static readonly Image LogoImage = LoadLogo();

        private static FontFamily LoadBrandFamily()
        {
            try
            {
                AddMemoryFont("DnwSus.Hinweis.Ubuntu-Regular.ttf");
                AddMemoryFont("DnwSus.Hinweis.Ubuntu-Bold.ttf");
                return FontCollection.Families.Length > 0 ? FontCollection.Families[0] : null;
            }
            catch { return null; }
        }

        private static void AddMemoryFont(string resource)
        {
            using (var stream = typeof(HintForm).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return;
                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length) read += stream.Read(data, read, data.Length - read);
                IntPtr buffer = Marshal.AllocCoTaskMem(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    FontCollection.AddMemoryFont(buffer, data.Length); // kopiert die Daten intern
                }
                finally { Marshal.FreeCoTaskMem(buffer); }
            }
        }

        private static Image LoadLogo()
        {
            try
            {
                using (var stream = typeof(HintForm).Assembly.GetManifestResourceStream("DnwSus.Hinweis.dnw-logo.png"))
                    return stream == null ? null : new Bitmap(Image.FromStream(stream));
            }
            catch { return null; }
        }

        /// <summary>Hausschrift Ubuntu, falls geladen; sonst Segoe UI als Rückfall.</summary>
        private static Font BrandFont(float size, FontStyle style)
        {
            try
            {
                if (BrandFamily != null && BrandFamily.IsStyleAvailable(style)) return new Font(BrandFamily, size, style);
                if (BrandFamily != null && BrandFamily.IsStyleAvailable(FontStyle.Regular)) return new Font(BrandFamily, size, FontStyle.Regular);
            }
            catch { }
            return new Font("Segoe UI", size, style);
        }

        private const string IntroText =
            "Auf diesem PC wurden Windows-Updates installiert. Damit sie wirksam werden, muss der PC neu gestartet werden.\n\n" +
            "Bitte speichern Sie vorher Ihre Arbeit.";

        private readonly Label _message;
        private readonly Label _info;
        private readonly FlowLayoutPanel _buttons;

        public int Result { get; private set; }

        private HintForm(string title)
        {
            Text = "Windows-Updates – " + title;
            Font = BrandFont(10f, FontStyle.Regular);
            BackColor = Color.White;
            ForeColor = TextColor;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, 0, 12);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };

            // Kopf im DNW-Design: weißer Hintergrund, transparentes Logo + Wortmarke „DNWsus" in Ubuntu.
            var header = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(16, 12, 16, 12)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var logoBox = new PictureBox
            {
                Image = LogoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(40, 40),
                Margin = new Padding(0, 0, 12, 0),
                Anchor = AnchorStyles.None
            };
            var brandLabel = new Label
            {
                Text = "DNWsus",
                Font = BrandFont(20f, FontStyle.Bold),
                ForeColor = BrandDark,
                AutoSize = true,
                Anchor = AnchorStyles.None
            };
            header.Controls.Add(logoBox, 0, 0);
            header.Controls.Add(brandLabel, 1, 0);

            var titleLabel = new Label
            {
                Text = title,
                Font = BrandFont(13f, FontStyle.Bold),
                ForeColor = BrandDark,
                AutoSize = true,
                Margin = new Padding(18, 14, 18, 4)
            };

            _message = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(18, 6, 18, 6) };
            _info = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted, Margin = new Padding(18, 2, 18, 10) };
            _buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                MaximumSize = new Size(500, 0),
                Margin = new Padding(14, 6, 14, 0)
            };

            var separator = new Panel
            {
                BackColor = Brand,
                Height = 2,
                Margin = new Padding(0),
                Dock = DockStyle.Fill,
                MinimumSize = new Size(520, 2)
            };

            layout.Controls.Add(header);
            layout.Controls.Add(separator);
            layout.Controls.Add(titleLabel);
            layout.Controls.Add(_message);
            layout.Controls.Add(_info);
            layout.Controls.Add(_buttons);
            Controls.Add(layout);
        }

        // ------------------------------------------------------------------ Gruppe 1

        public static HintForm AskGroup1()
        {
            var form = new HintForm("Neustart erforderlich");
            form._message.Text = IntroText;
            form._info.Text = "Der PC wird nur mit Ihrer Zustimmung neu gestartet. Bis dahin werden Sie alle 30 Minuten erinnert.";
            form.AddButton("Jetzt neu starten", true, () => form.Finish(Program.RebootNow));
            form.AddButton("Zeitpunkt wählen …", false, form.ShowTimePicker);
            form.AddButton("Später erinnern", false, () => form.Finish(0));
            return form;
        }

        private void ShowTimePicker()
        {
            _buttons.Controls.Clear();
            var now = DateTime.Now;
            var picker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dddd, dd.MM.yyyy  HH:mm 'Uhr'",
                ShowUpDown = true,
                MinDate = now,
                MaxDate = now.AddHours(24),
                Value = now.AddHours(1),
                Width = 300,
                Margin = new Padding(4, 6, 4, 6)
            };
            _info.Text = "Wählen Sie einen Zeitpunkt innerhalb der nächsten 24 Stunden. 5 Minuten vorher erhalten Sie noch einmal einen Hinweis.";
            _buttons.Controls.Add(picker);
            AddButton("Übernehmen", true, () =>
            {
                int minutes = (int)Math.Ceiling((picker.Value - DateTime.Now).TotalMinutes);
                Finish(Program.DelayBase + Math.Max(1, Math.Min(24 * 60, minutes)));
            });
            AddButton("Zurück", false, () => Finish(0));
        }

        // ------------------------------------------------------------------ Gruppe 2

        public static HintForm AskGroup2(DateTime deadline)
        {
            var form = new HintForm("Neustart erforderlich");
            form._message.Text = IntroText;
            form._info.Text = "Spätestens um " + deadline.ToString("HH:mm") + " Uhr wird der PC automatisch neu gestartet. Wann soll er neu starten?";
            form.AddButton("Jetzt", true, () => form.Finish(Program.RebootNow));
            foreach (int minutes in new[] { 15, 60, 120 })
            {
                var button = form.AddButton(minutes < 60 ? "In " + minutes + " Min." : "In " + minutes / 60 + " Std.", false,
                    () => form.Finish(Program.DelayBase + minutes));
                // Zeitpunkte nach der Frist nicht anbieten
                button.Enabled = DateTime.Now.AddMinutes(minutes) <= deadline.AddMinutes(1);
            }
            form.AddButton("Später erinnern", false, () => form.Finish(0));
            return form;
        }

        // ------------------------------------------------------------------ Countdown

        public static HintForm Countdown(DateTime rebootAt)
        {
            var form = new HintForm("Neustart in Kürze");
            form._info.Text = "Bitte speichern Sie jetzt Ihre Arbeit.";
            form.AddButton("Jetzt neu starten", true, () => form.Finish(Program.RebootNow));

            var timer = new Timer { Interval = 1000 };
            void Update()
            {
                var left = rebootAt - DateTime.Now;
                if (left <= TimeSpan.Zero)
                {
                    timer.Stop();
                    form.Finish(0); // den Neustart selbst löst der Agent aus
                    return;
                }
                form._message.Text = "Wegen der Installation von Windows-Updates startet der PC in " +
                                     ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00") + " Minuten neu.";
            }
            timer.Tick += (s, e) => Update();
            form.Shown += (s, e) => { Update(); timer.Start(); };
            form.FormClosed += (s, e) => timer.Dispose();
            return form;
        }

        // ------------------------------------------------------------------ Hilfen

        private Button AddButton(string text, bool primary, Action onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(110, 34),
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(4, 6, 4, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Brand : Color.FromArgb(240, 240, 240),
                ForeColor = primary ? Color.White : TextColor,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Brand : Color.FromArgb(200, 200, 200);
            button.Click += (s, e) => onClick();
            _buttons.Controls.Add(button);
            if (primary) AcceptButton = button;
            return button;
        }

        private void Finish(int result)
        {
            Result = result;
            Close();
        }
    }
}
