using System.Drawing;
using System.Windows.Forms;
using WindowsUpdateManager.Models;

namespace WindowsUpdateManager.Theme
{
    public static class DarkTheme
    {
        public static readonly Color Background   = ColorTranslator.FromHtml("#1E1E2E");
        public static readonly Color Surface      = ColorTranslator.FromHtml("#313244");
        public static readonly Color SurfaceLight = ColorTranslator.FromHtml("#3A3A54");
        public static readonly Color Accent       = ColorTranslator.FromHtml("#0078D4");
        public static readonly Color Text         = ColorTranslator.FromHtml("#CDD6F4");
        public static readonly Color TextMuted    = ColorTranslator.FromHtml("#6C7086");
        public static readonly Color RebootWarn   = ColorTranslator.FromHtml("#FAB387");
        public static readonly Color Error        = ColorTranslator.FromHtml("#F38BA8");
        public static readonly Color Success      = ColorTranslator.FromHtml("#A6E3A1");
        public static readonly Color Border       = ColorTranslator.FromHtml("#45475A");

        // Tinted row colors
        public static readonly Color AccentTint  = Color.FromArgb(255, 30, 50, 80);
        public static readonly Color SuccessTint = Color.FromArgb(255, 30, 60, 40);
        public static readonly Color ErrorTint   = Color.FromArgb(255, 70, 30, 40);
        public static readonly Color MutedTint   = Color.FromArgb(255, 40, 40, 55);

        public static void Apply(Control control)
        {
            ApplyRecursive(control);
        }

        private static void ApplyRecursive(Control control)
        {
            if (control is DataGridView)
            {
                StyleDataGridView((DataGridView)control);
                // Do not recurse into DataGridView children
                return;
            }
            else if (control is StatusStrip)
            {
                var ss = (StatusStrip)control;
                ss.BackColor = Surface;
                ss.ForeColor = Text;
                ss.Renderer  = new DarkToolStripRenderer();
                StyleToolStripItems(ss);
            }
            else if (control is ToolStrip)
            {
                var ts = (ToolStrip)control;
                ts.BackColor = Surface;
                ts.ForeColor = Text;
                ts.Renderer  = new DarkToolStripRenderer();
                StyleToolStripItems(ts);
            }
            else if (control is SplitContainer)
            {
                var sc = (SplitContainer)control;
                sc.BackColor         = Background;
                sc.ForeColor         = Text;
                sc.Panel1.BackColor  = Background;
                sc.Panel2.BackColor  = Background;
            }
            else if (control is TabPage)
            {
                var tp = (TabPage)control;
                tp.BackColor = Background;
                tp.ForeColor = Text;
            }
            else if (control is TabControl)
            {
                var tc = (TabControl)control;
                tc.BackColor = Background;
                tc.ForeColor = Text;
            }
            else if (control is RichTextBox)
            {
                var rtb = (RichTextBox)control;
                rtb.BackColor   = Surface;
                rtb.ForeColor   = Text;
                rtb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is TextBox)
            {
                var tb = (TextBox)control;
                tb.BackColor   = Surface;
                tb.ForeColor   = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox)
            {
                var cb = (ComboBox)control;
                cb.BackColor = Surface;
                cb.ForeColor = Text;
                cb.FlatStyle = FlatStyle.Flat;
            }
            else if (control is CheckBox)
            {
                var chk = (CheckBox)control;
                chk.BackColor = Background;
                chk.ForeColor = Text;
            }
            else if (control is Button)
            {
                var btn = (Button)control;
                btn.BackColor = Accent;
                btn.ForeColor = System.Drawing.Color.White;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor          = Accent;
                btn.FlatAppearance.MouseOverBackColor   = Color.FromArgb(255, 0, 100, 190);
                btn.FlatAppearance.MouseDownBackColor   = Color.FromArgb(255, 0, 60, 130);
            }
            else if (control is Label)
            {
                var lbl = (Label)control;
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = Text;
            }
            else if (control is Panel)
            {
                var pnl = (Panel)control;
                pnl.BackColor = Background;
                pnl.ForeColor = Text;
            }
            else if (control is Form)
            {
                control.BackColor = Background;
                control.ForeColor = Text;
            }
            else
            {
                control.BackColor = Background;
                control.ForeColor = Text;
            }

            // Recurse into children (skip DataGridView – already handled above)
            foreach (Control child in control.Controls)
            {
                ApplyRecursive(child);
            }
        }

        private static void StyleToolStripItems(ToolStrip ts)
        {
            foreach (ToolStripItem item in ts.Items)
            {
                item.BackColor = Surface;
                item.ForeColor = Text;

                if (item is ToolStripComboBox)
                {
                    var cb = (ToolStripComboBox)item;
                    cb.BackColor = Surface;
                    cb.ForeColor = Text;
                    if (cb.ComboBox != null)
                    {
                        cb.ComboBox.BackColor = Surface;
                        cb.ComboBox.ForeColor = Text;
                        cb.ComboBox.FlatStyle = FlatStyle.Flat;
                    }
                }
                else if (item is ToolStripStatusLabel)
                {
                    var sl = (ToolStripStatusLabel)item;
                    sl.BackColor = Surface;
                    sl.ForeColor = Text;
                }
            }
        }

        private static void StyleDataGridView(DataGridView dgv)
        {
            dgv.BackgroundColor = Background;
            dgv.GridColor       = Border;
            dgv.BorderStyle     = BorderStyle.FixedSingle;
            dgv.EnableHeadersVisualStyles  = false;
            dgv.RowHeadersVisible          = false;
            dgv.AutoSizeRowsMode           = DataGridViewAutoSizeRowsMode.None;
            dgv.ColumnHeadersBorderStyle   = DataGridViewHeaderBorderStyle.Single;

            dgv.DefaultCellStyle.BackColor          = Surface;
            dgv.DefaultCellStyle.ForeColor          = Text;
            dgv.DefaultCellStyle.SelectionBackColor = Accent;
            dgv.DefaultCellStyle.SelectionForeColor = Color.White;
            dgv.DefaultCellStyle.NullValue          = "--";

            dgv.AlternatingRowsDefaultCellStyle.BackColor          = SurfaceLight;
            dgv.AlternatingRowsDefaultCellStyle.ForeColor          = Text;
            dgv.AlternatingRowsDefaultCellStyle.SelectionBackColor = Accent;
            dgv.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;

            dgv.ColumnHeadersDefaultCellStyle.BackColor          = Background;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor          = Text;
            dgv.ColumnHeadersDefaultCellStyle.Font               = new Font(dgv.Font, FontStyle.Bold);
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background;
            dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;

            dgv.RowsDefaultCellStyle.BackColor = Surface;
            dgv.RowsDefaultCellStyle.ForeColor = Text;
        }

        public static void StyleDataGridViewRow(DataGridViewRow row, ServerStatus status)
        {
            Color backColor;
            Color foreColor = Text;

            switch (status)
            {
                case ServerStatus.Scanning:
                    backColor = Surface;
                    foreColor = Text;
                    break;
                case ServerStatus.Installing:
                    backColor = AccentTint;
                    break;
                case ServerStatus.Scheduled:
                    backColor = AccentTint;
                    foreColor = Accent;
                    break;
                case ServerStatus.Done:
                    backColor = SuccessTint;
                    break;
                case ServerStatus.Error:
                    backColor = ErrorTint;
                    break;
                case ServerStatus.Offline:
                    backColor = MutedTint;
                    foreColor = TextMuted;
                    break;
                default:
                    backColor = (row.Index % 2 == 0) ? Surface : SurfaceLight;
                    break;
            }

            row.DefaultCellStyle.BackColor          = backColor;
            row.DefaultCellStyle.ForeColor          = foreColor;
            row.DefaultCellStyle.SelectionBackColor = Accent;
            row.DefaultCellStyle.SelectionForeColor = Color.White;
        }
    }

    internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer()
            : base(new DarkColorTable())
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // No border
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            if (btn != null && btn.Pressed)
            {
                e.Graphics.FillRectangle(
                    new System.Drawing.SolidBrush(DarkTheme.Accent),
                    new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
            else if (btn != null && btn.Selected)
            {
                e.Graphics.FillRectangle(
                    new System.Drawing.SolidBrush(Color.FromArgb(255, 50, 60, 80)),
                    new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
            else
            {
                e.Graphics.FillRectangle(
                    new System.Drawing.SolidBrush(DarkTheme.Surface),
                    new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin               { get { return DarkTheme.Surface; } }
        public override Color MenuStripGradientEnd                 { get { return DarkTheme.Surface; } }
        public override Color ToolStripGradientBegin               { get { return DarkTheme.Surface; } }
        public override Color ToolStripGradientMiddle              { get { return DarkTheme.Surface; } }
        public override Color ToolStripGradientEnd                 { get { return DarkTheme.Surface; } }
        public override Color ToolStripBorder                      { get { return DarkTheme.Border; } }
        public override Color ToolStripContentPanelGradientBegin   { get { return DarkTheme.Background; } }
        public override Color ToolStripContentPanelGradientEnd     { get { return DarkTheme.Background; } }
        public override Color ButtonSelectedHighlight              { get { return Color.FromArgb(255, 50, 60, 80); } }
        public override Color ButtonSelectedHighlightBorder        { get { return DarkTheme.Border; } }
        public override Color ButtonPressedHighlight               { get { return DarkTheme.Accent; } }
        public override Color ButtonPressedHighlightBorder         { get { return DarkTheme.Accent; } }
        public override Color ButtonCheckedHighlight               { get { return DarkTheme.Accent; } }
        public override Color ButtonCheckedHighlightBorder         { get { return DarkTheme.Accent; } }
        public override Color ButtonPressedBorder                  { get { return DarkTheme.Accent; } }
        public override Color ButtonSelectedBorder                 { get { return DarkTheme.Border; } }
        public override Color ButtonCheckedGradientBegin           { get { return DarkTheme.Accent; } }
        public override Color ButtonCheckedGradientMiddle          { get { return DarkTheme.Accent; } }
        public override Color ButtonCheckedGradientEnd             { get { return DarkTheme.Accent; } }
        public override Color ButtonSelectedGradientBegin          { get { return Color.FromArgb(255, 50, 60, 80); } }
        public override Color ButtonSelectedGradientMiddle         { get { return Color.FromArgb(255, 50, 60, 80); } }
        public override Color ButtonSelectedGradientEnd            { get { return Color.FromArgb(255, 50, 60, 80); } }
        public override Color ButtonPressedGradientBegin           { get { return DarkTheme.Accent; } }
        public override Color ButtonPressedGradientMiddle          { get { return DarkTheme.Accent; } }
        public override Color ButtonPressedGradientEnd             { get { return DarkTheme.Accent; } }
        public override Color SeparatorDark                        { get { return DarkTheme.Border; } }
        public override Color SeparatorLight                       { get { return DarkTheme.Border; } }
        public override Color ImageMarginGradientBegin             { get { return DarkTheme.Surface; } }
        public override Color ImageMarginGradientMiddle            { get { return DarkTheme.Surface; } }
        public override Color ImageMarginGradientEnd               { get { return DarkTheme.Surface; } }
    }
}
