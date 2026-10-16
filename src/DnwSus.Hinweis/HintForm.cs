using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnwSus.Hinweis
{
    /// <summary>Helles Hinweisfenster im DNW-Blau. Logo folgt, sobald eine transparente Version vorliegt.</summary>
    internal sealed class HintForm : Form
    {
        private static readonly Color Brand = Color.FromArgb(0, 120, 212);
        private static readonly Color BrandDark = Color.FromArgb(0, 40, 110);
        private static readonly Color TextColor = Color.FromArgb(30, 30, 30);
        private static readonly Color Muted = Color.FromArgb(100, 100, 100);

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
            Font = new Font("Segoe UI", 10f);
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

            var header = new Label
            {
                Text = "DNW  ·  Windows-Updates",
                ForeColor = Color.White,
                BackColor = Brand,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(520, 46),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Margin = new Padding(0)
            };

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = BrandDark,
                AutoSize = true,
                Margin = new Padding(18, 16, 18, 4)
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

            layout.Controls.Add(header);
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
