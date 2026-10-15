using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsUpdateManager.Controls;
using WindowsUpdateManager.Helpers;
using WindowsUpdateManager.Models;
using WindowsUpdateManager.Services;
using WindowsUpdateManager.Theme;

namespace WindowsUpdateManager.Forms
{
    public class MainForm : Form
    {
        // ---- Services / data ----
        private List<ServerInfo>       _servers    = new List<ServerInfo>();
        private RemoteSessionService   _remoteService;
        private AdDiscoveryService     _adService;
        private UpdateScannerService   _scannerService;
        private UpdateInstallerService _installerService;
        private CsvLogger              _logger;

        // Each operation (bulk or single-server) owns its own CancellationTokenSource so that
        // starting a new job never cancels an already-running one. Tracked here only so
        // FormClosing can cancel everything still in flight.
        private List<CancellationTokenSource> _activeCtsList = new List<CancellationTokenSource>();

        // Server names currently involved in a scan/install job. Any new job (bulk or single)
        // filters these out instead of blocking, so unrelated servers can run in parallel.
        private HashSet<string> _busyServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int  _activeOperationCount = 0;
        private bool _ouLoaded = false;

        // ---- ToolStrip controls ----
        private ToolStrip          _toolStrip;
        private ToolStripButton    _btnLoadServers;
        private ToolStripComboBox  _cboOuFilter;
        private ToolStripButton    _btnScanUpdates;
        private ToolStripButton    _btnInstallUpdates;
        private ToolStripButton    _btnDeselectReboot;
        private ToolStripLabel     _lblToolStatus;
        private NumericUpDown      _nudParallel;

        // ---- Main content panel (DockStyle.Fill on form; holds RebootBar + outerSplit)
        // ToolStrip lives directly on the form; putting anything else alongside it causes
        // layout glitches because ToolStrip auto-sizes its height asynchronously.
        // Isolating everything else inside _pnlMain avoids that entirely. ----
        private Panel              _pnlMain;

        // ---- Reboot-Bar (always visible inside _pnlMain) ----
        private Panel          _pnlRebootBar;
        private DateTimePicker _dtpGlobalReboot;
        private Button         _btnApplyGlobalReboot;
        private DateTimePicker _dtpGlobalInstall;
        private Button         _btnApplyGlobalInstall;

        // ---- Grid – Servers ----
        private DataGridView _dgvServers;
        private Label        _lblServersHeader;
        private DataGridViewButtonColumn _colScanBtn;
        private DataGridViewButtonColumn _colInstallBtn;
        private bool _selectAllChecked = false;

        // ---- Grid – Updates ----
        private DataGridView _dgvUpdates;
        private Label        _lblUpdateHeader;

        // ---- Log panel ----
        private RichTextBox _rtbLog;

        // ---- StatusStrip ----
        private StatusStrip          _statusStrip;
        private ToolStripStatusLabel _lblStatusBar;
        private ToolStripProgressBar _progressBar;

        // Suppress re-entrant cell-change events
        private bool _suppressCellChange = false;

        // -----------------------------------------------------------------------
        public MainForm()
        {
            _remoteService    = new RemoteSessionService();
            _adService        = new AdDiscoveryService();
            _scannerService   = new UpdateScannerService(_remoteService);
            _logger           = new CsvLogger(Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory, "Logs"));
            _installerService = new UpdateInstallerService(_remoteService, _logger);

            BuildUI();
            DarkTheme.Apply(this);
            SetStatus("Bereit.");
            AppendLog("Log-Datei: " + _logger.LogFilePath);
        }

        // =======================================================================
        // UI Construction
        // =======================================================================
        private void BuildUI()
        {
            Text            = "DNWSUS v1.0.34";
            Size            = new Size(1200, 750);
            StartPosition   = FormStartPosition.CenterScreen;
            MinimumSize     = new Size(900, 600);
            BackColor       = DarkTheme.Background;
            ForeColor       = DarkTheme.Text;
            Font            = new Font("Segoe UI", 9f);

            // Layout strategy (WinForms docks in reverse Controls index order):
            //   form[3] = _toolStrip    (Top, highest index → very top)
            //   form[2] = _statusStrip  (Bottom)
            //   form[1] = _pnlMain      (Fill → everything below toolbar/rebootbar)
            //   form[0] = _pnlRebootBar (Top, lowest index → just below toolbar)
            //
            // SplitContainer caches its bounds and doesn't update when ToolStrip
            // auto-resizes its height. Wrapping it in _pnlMain (a plain Panel) shields
            // it: _pnlMain repositions correctly, and outerSplit fills _pnlMain.
            _pnlMain           = new Panel();
            _pnlMain.Dock      = DockStyle.Fill;
            _pnlMain.BackColor = DarkTheme.Background;

            BuildRebootBar();   // Controls.Add(_pnlRebootBar) → form[0]
            Controls.Add(_pnlMain);                             // form[1]
            BuildStatusStrip(); // Controls.Add(_statusStrip)  → form[2]
            BuildToolStrip();   // Controls.Add(_toolStrip)    → form[3]
            BuildMainSplit();   // _pnlMain.Controls.Add(outerSplit)

            Shown += new EventHandler(MainForm_Shown);
            FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Force a layout pass after all controls have measured their final sizes
            // (ToolStrip auto-sizing happens asynchronously and can leave the Fill
            // controls at stale positions on the first pass).
            PerformLayout();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var cts in _activeCtsList)
                cts.Cancel();
        }

        // ---- ToolStrip ----
        private void BuildToolStrip()
        {
            _toolStrip = new ToolStrip();
            _toolStrip.Dock = DockStyle.Top;

            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnLoadServers = new ToolStripButton("Server aus AD laden");
            _btnLoadServers.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnLoadServers.Click += new EventHandler(BtnLoadServers_Click);
            _toolStrip.Items.Add(_btnLoadServers);

            _toolStrip.Items.Add(new ToolStripSeparator());

            var lblOu = new ToolStripLabel("OU-Filter:");
            lblOu.ForeColor = DarkTheme.TextMuted;
            _toolStrip.Items.Add(lblOu);

            _cboOuFilter = new ToolStripComboBox();
            _cboOuFilter.AutoSize    = false;
            _cboOuFilter.Width       = 280;
            _cboOuFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboOuFilter.Items.Add("Alle OUs");
            _cboOuFilter.SelectedIndex = 0;
            _toolStrip.Items.Add(_cboOuFilter);

            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnScanUpdates = new ToolStripButton("Updates suchen");
            _btnScanUpdates.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnScanUpdates.Enabled      = false;
            _btnScanUpdates.Click += new EventHandler(BtnScanUpdates_Click);
            _toolStrip.Items.Add(_btnScanUpdates);

            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnInstallUpdates = new ToolStripButton("Updates installieren");
            _btnInstallUpdates.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnInstallUpdates.Enabled      = false;
            _btnInstallUpdates.Click += new EventHandler(BtnInstallUpdates_Click);
            _toolStrip.Items.Add(_btnInstallUpdates);

            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnDeselectReboot = new ToolStripButton("Abwählen: Reboot-Server");
            _btnDeselectReboot.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnDeselectReboot.Enabled      = false;
            _btnDeselectReboot.Click += new EventHandler(BtnDeselectReboot_Click);
            _toolStrip.Items.Add(_btnDeselectReboot);

            _toolStrip.Items.Add(new ToolStripSeparator());

            var lblParallel = new ToolStripLabel("Parallele Verbindungen:");
            lblParallel.ForeColor = DarkTheme.TextMuted;
            _toolStrip.Items.Add(lblParallel);

            _nudParallel = new NumericUpDown();
            _nudParallel.Minimum   = 1;
            _nudParallel.Maximum   = 50;
            _nudParallel.Value     = 20;
            _nudParallel.Width     = 50;
            _nudParallel.BackColor = DarkTheme.Surface;
            _nudParallel.ForeColor = DarkTheme.Text;
            _nudParallel.BorderStyle = BorderStyle.FixedSingle;
            _nudParallel.Font      = new Font("Segoe UI", 9f);
            _toolStrip.Items.Add(new ToolStripControlHost(_nudParallel));

            _toolStrip.Items.Add(new ToolStripSeparator());

            _lblToolStatus = new ToolStripLabel("");
            _lblToolStatus.ForeColor = DarkTheme.TextMuted;
            _toolStrip.Items.Add(_lblToolStatus);

            Controls.Add(_toolStrip);
        }

        // ---- Timing-Bar (Updatestart + Reboot, side by side, always visible inside _pnlMain) ----
        private void BuildRebootBar()
        {
            _pnlRebootBar            = new Panel();
            _pnlRebootBar.Dock       = DockStyle.Top;
            _pnlRebootBar.Height     = 38;
            _pnlRebootBar.BackColor  = DarkTheme.Surface;

            // Updatestart (alle)
            var lblInstallGlobal = new Label();
            lblInstallGlobal.Text      = "Updatestart (alle):";
            lblInstallGlobal.ForeColor = DarkTheme.Text;
            lblInstallGlobal.BackColor = Color.Transparent;
            lblInstallGlobal.AutoSize  = true;
            lblInstallGlobal.Location  = new Point(8, 10);

            _dtpGlobalInstall = new DateTimePicker();
            _dtpGlobalInstall.Format       = DateTimePickerFormat.Custom;
            _dtpGlobalInstall.CustomFormat = "dd.MM.yyyy HH:mm";
            _dtpGlobalInstall.ShowUpDown   = true;
            _dtpGlobalInstall.Value        = DateTime.Today.AddDays(1).AddHours(1);
            _dtpGlobalInstall.Width        = 130;
            _dtpGlobalInstall.Location     = new Point(140, 7);
            _dtpGlobalInstall.BackColor    = DarkTheme.Background;
            _dtpGlobalInstall.ForeColor    = DarkTheme.Text;
            _dtpGlobalInstall.CalendarMonthBackground = DarkTheme.Background;
            _dtpGlobalInstall.CalendarForeColor       = DarkTheme.Text;

            _btnApplyGlobalInstall = new Button();
            _btnApplyGlobalInstall.Text      = "Übernehmen";
            _btnApplyGlobalInstall.BackColor = DarkTheme.Accent;
            _btnApplyGlobalInstall.ForeColor = Color.White;
            _btnApplyGlobalInstall.FlatStyle = FlatStyle.Flat;
            _btnApplyGlobalInstall.FlatAppearance.BorderColor = DarkTheme.Accent;
            _btnApplyGlobalInstall.Location  = new Point(278, 6);
            _btnApplyGlobalInstall.AutoSize  = true;
            _btnApplyGlobalInstall.Height    = 26;
            _btnApplyGlobalInstall.Font      = new Font("Segoe UI", 9f);
            _btnApplyGlobalInstall.Click    += new EventHandler(BtnApplyGlobalInstall_Click);

            // Reboot-Zeit (alle) – same row, further right
            var lblGlobal = new Label();
            lblGlobal.Text      = "Reboot-Zeit (alle):";
            lblGlobal.ForeColor = DarkTheme.Text;
            lblGlobal.BackColor = Color.Transparent;
            lblGlobal.AutoSize  = true;
            lblGlobal.Location  = new Point(390, 10);

            _dtpGlobalReboot = new DateTimePicker();
            _dtpGlobalReboot.Format       = DateTimePickerFormat.Custom;
            _dtpGlobalReboot.CustomFormat = "dd.MM.yyyy HH:mm";
            _dtpGlobalReboot.ShowUpDown   = true;
            _dtpGlobalReboot.Value        = DateTime.Today.AddDays(1).AddHours(2);
            _dtpGlobalReboot.Width        = 130;
            _dtpGlobalReboot.Location     = new Point(520, 7);
            _dtpGlobalReboot.BackColor    = DarkTheme.Background;
            _dtpGlobalReboot.ForeColor    = DarkTheme.Text;
            _dtpGlobalReboot.CalendarMonthBackground = DarkTheme.Background;
            _dtpGlobalReboot.CalendarForeColor       = DarkTheme.Text;

            _btnApplyGlobalReboot = new Button();
            _btnApplyGlobalReboot.Text      = "Übernehmen";
            _btnApplyGlobalReboot.BackColor = DarkTheme.Accent;
            _btnApplyGlobalReboot.ForeColor = Color.White;
            _btnApplyGlobalReboot.FlatStyle = FlatStyle.Flat;
            _btnApplyGlobalReboot.FlatAppearance.BorderColor = DarkTheme.Accent;
            _btnApplyGlobalReboot.Location  = new Point(658, 6);
            _btnApplyGlobalReboot.AutoSize  = true;
            _btnApplyGlobalReboot.Height    = 26;
            _btnApplyGlobalReboot.Font      = new Font("Segoe UI", 9f);
            _btnApplyGlobalReboot.Click    += new EventHandler(BtnApplyGlobalReboot_Click);

            _pnlRebootBar.Controls.AddRange(new Control[]
                { lblInstallGlobal, _dtpGlobalInstall, _btnApplyGlobalInstall,
                  lblGlobal, _dtpGlobalReboot, _btnApplyGlobalReboot });

            Controls.Add(_pnlRebootBar);
        }

        // ---- StatusStrip ----
        private void BuildStatusStrip()
        {
            _statusStrip = new StatusStrip();
            _statusStrip.Dock = DockStyle.Bottom;

            _lblStatusBar = new ToolStripStatusLabel("Bereit.");
            _lblStatusBar.Spring    = true;
            _lblStatusBar.TextAlign = ContentAlignment.MiddleLeft;

            _progressBar = new ToolStripProgressBar();
            _progressBar.AutoSize = false;
            _progressBar.Width    = 200;
            _progressBar.Visible  = false;
            _progressBar.Style    = ProgressBarStyle.Marquee;

            _statusStrip.Items.Add(_lblStatusBar);
            _statusStrip.Items.Add(_progressBar);

            Controls.Add(_statusStrip);
        }

        // ---- Main SplitContainer ----
        private void BuildMainSplit()
        {
            var outerSplit = new SplitContainer();
            outerSplit.Dock             = DockStyle.Fill;
            outerSplit.Orientation      = Orientation.Horizontal;
            outerSplit.SplitterDistance = 420;
            outerSplit.BackColor        = DarkTheme.Background;

            var innerSplit = new SplitContainer();
            innerSplit.Dock             = DockStyle.Fill;
            innerSplit.Orientation      = Orientation.Vertical;
            innerSplit.SplitterDistance = 600;
            innerSplit.BackColor        = DarkTheme.Background;

            innerSplit.Panel1.Controls.Add(BuildServerPanel());
            innerSplit.Panel2.Controls.Add(BuildUpdatePanel());

            outerSplit.Panel1.Controls.Add(innerSplit);
            outerSplit.Panel2.Controls.Add(BuildLogPanel());

            _pnlMain.Controls.Add(outerSplit);
        }

        // ---- Server panel ----
        private Panel BuildServerPanel()
        {
            var panel = new Panel();
            panel.Dock      = DockStyle.Fill;
            panel.BackColor = DarkTheme.Background;

            _lblServersHeader = new Label();
            _lblServersHeader.Text      = "Server";
            _lblServersHeader.Font      = new Font("Segoe UI", 10f, FontStyle.Bold);
            _lblServersHeader.ForeColor = DarkTheme.Accent;
            _lblServersHeader.BackColor = DarkTheme.Background;
            _lblServersHeader.Dock      = DockStyle.Top;
            _lblServersHeader.Height    = 28;
            _lblServersHeader.TextAlign = ContentAlignment.MiddleLeft;
            _lblServersHeader.Padding   = new Padding(4, 0, 0, 0);

            _dgvServers = new DataGridView();
            _dgvServers.Dock                     = DockStyle.Fill;
            _dgvServers.AllowUserToAddRows       = false;
            _dgvServers.AllowUserToDeleteRows    = false;
            _dgvServers.AllowUserToResizeRows    = false;
            _dgvServers.MultiSelect              = false;
            _dgvServers.SelectionMode            = DataGridViewSelectionMode.FullRowSelect;
            _dgvServers.ScrollBars               = ScrollBars.Both;
            _dgvServers.ColumnHeadersHeight      = 26;
            _dgvServers.RowTemplate.Height       = 24;

            var colCheck = new DataGridViewCheckBoxColumn();
            colCheck.HeaderText   = "";
            colCheck.Width        = 30;
            colCheck.MinimumWidth = 30;
            colCheck.Resizable    = DataGridViewTriState.False;

            var colName = new DataGridViewTextBoxColumn();
            colName.HeaderText = "Server";
            colName.Width      = 150;
            colName.ReadOnly   = true;

            var colOs = new DataGridViewTextBoxColumn();
            colOs.HeaderText = "Betriebssystem";
            colOs.Width      = 180;
            colOs.ReadOnly   = true;

            var colStatus = new DataGridViewTextBoxColumn();
            colStatus.HeaderText = "Status";
            colStatus.Width      = 100;
            colStatus.ReadOnly   = true;

            var colUpdates = new DataGridViewTextBoxColumn();
            colUpdates.HeaderText = "Updates";
            colUpdates.Width      = 60;
            colUpdates.ReadOnly   = true;

            var colReboot = new DataGridViewTextBoxColumn();
            colReboot.HeaderText = "Reboot";
            colReboot.Width      = 70;
            colReboot.ReadOnly   = true;

            var colRebootTime = new DataGridViewDateTimePickerColumn();
            colRebootTime.HeaderText = "Reboot-Zeit";
            colRebootTime.Width      = 140;

            var colUptime = new DataGridViewTextBoxColumn();
            colUptime.HeaderText = "Uptime";
            colUptime.Width      = 100;
            colUptime.ReadOnly   = true;

            var colInstallTime = new DataGridViewInstallTimePickerColumn();
            colInstallTime.HeaderText = "Updatestart";
            colInstallTime.Width      = 140;

            _colScanBtn = new DataGridViewButtonColumn();
            _colScanBtn.HeaderText = "Suche";
            _colScanBtn.Text       = "Suchen";
            _colScanBtn.UseColumnTextForButtonValue = true;
            _colScanBtn.Width      = 80;
            _colScanBtn.FlatStyle  = FlatStyle.Flat;
            _colScanBtn.DefaultCellStyle.BackColor          = DarkTheme.Accent;
            _colScanBtn.DefaultCellStyle.ForeColor          = Color.White;
            _colScanBtn.DefaultCellStyle.SelectionBackColor = DarkTheme.Accent;
            _colScanBtn.DefaultCellStyle.SelectionForeColor = Color.White;

            _colInstallBtn = new DataGridViewButtonColumn();
            _colInstallBtn.HeaderText = "Install";
            _colInstallBtn.Text       = "Installieren";
            _colInstallBtn.UseColumnTextForButtonValue = true;
            _colInstallBtn.Width      = 90;
            _colInstallBtn.FlatStyle  = FlatStyle.Flat;
            _colInstallBtn.DefaultCellStyle.BackColor          = DarkTheme.Accent;
            _colInstallBtn.DefaultCellStyle.ForeColor          = Color.White;
            _colInstallBtn.DefaultCellStyle.SelectionBackColor = DarkTheme.Accent;
            _colInstallBtn.DefaultCellStyle.SelectionForeColor = Color.White;

            _dgvServers.Columns.AddRange(new DataGridViewColumn[]
            {
                colCheck, colName, colOs, colStatus, colUpdates, colReboot, colUptime, colInstallTime, colRebootTime,
                _colScanBtn, _colInstallBtn
            });

            _dgvServers.SelectionChanged    += new EventHandler(DgvServers_SelectionChanged);
            _dgvServers.CellValueChanged    += new DataGridViewCellEventHandler(DgvServers_CellValueChanged);
            _dgvServers.CellFormatting      += new DataGridViewCellFormattingEventHandler(DgvServers_CellFormatting);
            _dgvServers.CurrentCellDirtyStateChanged += new EventHandler(DgvServers_DirtyStateChanged);
            _dgvServers.CellPainting        += new DataGridViewCellPaintingEventHandler(DgvServers_CellPainting);
            _dgvServers.CellMouseClick      += new DataGridViewCellMouseEventHandler(DgvServers_CellMouseClick);
            _dgvServers.CellContentClick    += new DataGridViewCellEventHandler(DgvServers_CellContentClick);

            panel.Controls.Add(_dgvServers);
            panel.Controls.Add(_lblServersHeader);

            return panel;
        }

        // ---- Update panel ----
        private Panel BuildUpdatePanel()
        {
            var panel = new Panel();
            panel.Dock      = DockStyle.Fill;
            panel.BackColor = DarkTheme.Background;

            _lblUpdateHeader = new Label();
            _lblUpdateHeader.Text      = "Updates für: (kein Server gewählt)";
            _lblUpdateHeader.Font      = new Font("Segoe UI", 10f, FontStyle.Bold);
            _lblUpdateHeader.ForeColor = DarkTheme.Accent;
            _lblUpdateHeader.BackColor = DarkTheme.Background;
            _lblUpdateHeader.Dock      = DockStyle.Top;
            _lblUpdateHeader.Height    = 28;
            _lblUpdateHeader.TextAlign = ContentAlignment.MiddleLeft;
            _lblUpdateHeader.Padding   = new Padding(4, 0, 0, 0);

            _dgvUpdates = new DataGridView();
            _dgvUpdates.Dock                     = DockStyle.Fill;
            _dgvUpdates.AllowUserToAddRows       = false;
            _dgvUpdates.AllowUserToDeleteRows    = false;
            _dgvUpdates.AllowUserToResizeRows    = false;
            _dgvUpdates.MultiSelect              = false;
            _dgvUpdates.SelectionMode            = DataGridViewSelectionMode.FullRowSelect;
            _dgvUpdates.ScrollBars               = ScrollBars.Both;
            _dgvUpdates.ColumnHeadersHeight      = 26;
            _dgvUpdates.RowTemplate.Height       = 24;

            var colUCheck = new DataGridViewCheckBoxColumn();
            colUCheck.HeaderText   = "✓";
            colUCheck.Width        = 30;
            colUCheck.MinimumWidth = 30;
            colUCheck.Resizable    = DataGridViewTriState.False;

            var colKB = new DataGridViewTextBoxColumn();
            colKB.HeaderText = "KB-Artikel";
            colKB.Width      = 110;
            colKB.ReadOnly   = true;

            var colTitle = new DataGridViewTextBoxColumn();
            colTitle.HeaderText   = "Titel";
            colTitle.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colTitle.ReadOnly     = true;

            var colSize = new DataGridViewTextBoxColumn();
            colSize.HeaderText = "Größe (MB)";
            colSize.Width      = 80;
            colSize.ReadOnly   = true;

            var colUReboot = new DataGridViewTextBoxColumn();
            colUReboot.HeaderText = "Reboot";
            colUReboot.Width      = 70;
            colUReboot.ReadOnly   = true;

            var colCategory = new DataGridViewTextBoxColumn();
            colCategory.HeaderText = "Kategorie";
            colCategory.Width      = 130;
            colCategory.ReadOnly   = true;

            _dgvUpdates.Columns.AddRange(new DataGridViewColumn[]
            {
                colUCheck, colKB, colTitle, colSize, colUReboot, colCategory
            });

            _dgvUpdates.CellValueChanged += new DataGridViewCellEventHandler(DgvUpdates_CellValueChanged);
            _dgvUpdates.CurrentCellDirtyStateChanged += new EventHandler(DgvUpdates_DirtyStateChanged);

            panel.Controls.Add(_dgvUpdates);
            panel.Controls.Add(_lblUpdateHeader);

            return panel;
        }

        // ---- Log panel ----
        private Panel BuildLogPanel()
        {
            var panel = new Panel();
            panel.Dock      = DockStyle.Fill;
            panel.BackColor = DarkTheme.Background;

            var headerPanel = new Panel();
            headerPanel.Dock      = DockStyle.Top;
            headerPanel.Height    = 30;
            headerPanel.BackColor = DarkTheme.Background;

            var lblLog = new Label();
            lblLog.Text      = "Installations-Log";
            lblLog.Font      = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblLog.ForeColor = DarkTheme.Accent;
            lblLog.BackColor = Color.Transparent;
            lblLog.AutoSize  = true;
            lblLog.Location  = new Point(4, 6);

            var btnOpenLog = new Button();
            btnOpenLog.Text      = "Log öffnen";
            btnOpenLog.BackColor = DarkTheme.Surface;
            btnOpenLog.ForeColor = DarkTheme.Text;
            btnOpenLog.FlatStyle = FlatStyle.Flat;
            btnOpenLog.Anchor    = AnchorStyles.Right | AnchorStyles.Top;
            btnOpenLog.Size      = new Size(100, 24);
            btnOpenLog.FlatAppearance.BorderColor = DarkTheme.Border;
            btnOpenLog.Click += new EventHandler(BtnOpenLog_Click);

            headerPanel.Controls.Add(lblLog);
            headerPanel.Controls.Add(btnOpenLog);
            headerPanel.Resize += (s, e) =>
            {
                btnOpenLog.Location = new Point(headerPanel.Width - 110, 3);
            };
            // Set initial position
            btnOpenLog.Location = new Point(400, 3);

            _rtbLog = new RichTextBox();
            _rtbLog.Dock        = DockStyle.Fill;
            _rtbLog.ReadOnly    = true;
            _rtbLog.BackColor   = DarkTheme.Surface;
            _rtbLog.ForeColor   = DarkTheme.Text;
            _rtbLog.Font        = new Font("Consolas", 9f);
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.ScrollBars  = RichTextBoxScrollBars.Both;
            _rtbLog.WordWrap    = false;

            panel.Controls.Add(_rtbLog);
            panel.Controls.Add(headerPanel);

            return panel;
        }

        // =======================================================================
        // Button event handlers
        // =======================================================================
        private async void BtnLoadServers_Click(object sender, EventArgs e)
        {
            await LoadServersAsync();
        }

        private async void BtnScanUpdates_Click(object sender, EventArgs e)
        {
            await ScanUpdatesAsync();
        }

        private async void BtnInstallUpdates_Click(object sender, EventArgs e)
        {
            await InstallUpdatesAsync();
        }

        private async void DgvServers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row    = _dgvServers.Rows[e.RowIndex];
            var server = row.Tag as ServerInfo;
            if (server == null) return;

            if (e.ColumnIndex == _colScanBtn.Index)
            {
                await ScanSingleServerAsync(server);
            }
            else if (e.ColumnIndex == _colInstallBtn.Index)
            {
                await InstallSingleServerAsync(server);
            }
        }

        // =======================================================================
        // Data Loading
        // =======================================================================
        private async Task LoadServersAsync()
        {
            BeginOperation("Lade Server aus Active Directory...");

            var cts = new CancellationTokenSource();
            _activeCtsList.Add(cts);

            try
            {
                if (!_ouLoaded)
                {
                    SetStatus("Lade OUs...");
                    try
                    {
                        var ous = await _adService.GetOUsAsync();
                        this.InvokeIfRequired(() =>
                        {
                            _cboOuFilter.Items.Clear();
                            _cboOuFilter.Items.Add("Alle OUs");
                            foreach (var ou in ous)
                                _cboOuFilter.Items.Add(ou);
                            _cboOuFilter.SelectedIndex = 0;
                        });
                        _ouLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        AppendLog("[WARNUNG] OUs konnten nicht geladen werden: " + ex.Message);
                    }
                }

                string selectedOu = null;
                this.InvokeIfRequired(() =>
                {
                    if (_cboOuFilter.SelectedIndex > 0)
                        selectedOu = _cboOuFilter.SelectedItem != null
                            ? _cboOuFilter.SelectedItem.ToString()
                            : null;
                });

                SetStatus("Lade Server...");
                var servers = await _adService.GetServersAsync(selectedOu);

                this.InvokeIfRequired(() =>
                {
                    _servers = servers;
                    RefreshServerGrid();
                    RefreshToolbarButtonStates();
                    SetStatus(servers.Count + " Server geladen.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + servers.Count + " Server aus AD geladen.");
                });
            }
            catch (Exception ex)
            {
                this.InvokeIfRequired(() =>
                {
                    SetStatus("Fehler beim Laden der Server.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] FEHLER beim Laden: " + ex.Message);
                    MessageBox.Show(
                        "Fehler beim Laden der Server aus dem Active Directory:\n\n" + ex.Message,
                        "AD-Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            finally
            {
                _activeCtsList.Remove(cts);
                EndOperation();
            }
        }

        private async Task ScanUpdatesAsync()
        {
            if (_servers.Count == 0) return;

            var busySkipped = _servers.FindAll(s => _busyServers.Contains(s.Name));
            var targets      = _servers.FindAll(s => !_busyServers.Contains(s.Name));

            if (targets.Count == 0)
            {
                MessageBox.Show("Alle Server sind bereits in Bearbeitung.", "Hinweis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (busySkipped.Count > 0)
            {
                AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + busySkipped.Count +
                          " Server übersprungen (bereits in Bearbeitung): " +
                          string.Join(", ", busySkipped.ConvertAll(s => s.Name).ToArray()));
            }

            BeginOperation("Scanne Server auf Updates...");
            MarkBusy(targets);

            foreach (var server in targets)
            {
                server.Status = ServerStatus.Unknown;
                server.AvailableUpdates.Clear();
                server.ErrorMessage  = null;
                server.StatusMessage = "Ausstehend";
            }

            this.InvokeIfRequired(() => RefreshServerGrid());

            var cts = new CancellationTokenSource();
            _activeCtsList.Add(cts);

            try
            {
                var progress = new Progress<ServerInfo>(server =>
                {
                    this.InvokeIfRequired(() => UpdateServerRow(server));
                });

                await _scannerService.ScanServersAsync(targets, progress, (int)_nudParallel.Value, cts.Token);

                int totalUpdates       = 0;
                int serversWithUpdates = 0;
                foreach (var s in targets)
                {
                    totalUpdates += s.AvailableUpdates.Count;
                    if (s.AvailableUpdates.Count > 0) serversWithUpdates++;
                }

                this.InvokeIfRequired(() =>
                {
                    SetStatus("Scan abgeschlossen. " + serversWithUpdates + " Server mit Updates, " + totalUpdates + " Updates gesamt.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Scan abgeschlossen. " +
                              serversWithUpdates + "/" + targets.Count + " Server mit Updates (" + totalUpdates + " Updates gesamt).");
                    RefreshToolbarButtonStates();
                });
            }
            catch (OperationCanceledException)
            {
                this.InvokeIfRequired(() => SetStatus("Scan abgebrochen."));
            }
            catch (Exception ex)
            {
                this.InvokeIfRequired(() =>
                {
                    SetStatus("Fehler beim Scan.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] FEHLER beim Scan: " + ex.Message);
                });
            }
            finally
            {
                UnmarkBusy(targets);
                _activeCtsList.Remove(cts);
                EndOperation();
            }
        }

        private async Task InstallUpdatesAsync()
        {
            var candidates = _servers.FindAll(s => s.Selected && s.AvailableUpdates.Count > 0);
            if (candidates.Count == 0)
            {
                MessageBox.Show("Keine Server mit ausgewählten Updates vorhanden.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var busySkipped     = candidates.FindAll(s => _busyServers.Contains(s.Name));
            var selectedServers = candidates.FindAll(s => !_busyServers.Contains(s.Name));

            if (selectedServers.Count == 0)
            {
                MessageBox.Show("Alle ausgewählten Server mit Updates sind bereits in Bearbeitung.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int totalUpdates = 0;
            foreach (var s in selectedServers)
                totalUpdates += s.AvailableUpdates.FindAll(u => u.Selected).Count;

            var confirm = MessageBox.Show(
                "Updates werden auf " + selectedServers.Count + " Server(n) installiert (" + totalUpdates + " Updates).\n\nFortfahren?",
                "Installation bestätigen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            // Warn if any server has reboot scheduled before install start
            var timingConflicts = new System.Text.StringBuilder();
            foreach (var s in selectedServers)
            {
                if (s.InstallScheduledAt.HasValue && s.RebootScheduledAt.HasValue
                    && s.RebootScheduledAt.Value < s.InstallScheduledAt.Value)
                {
                    timingConflicts.AppendLine(string.Format("  {0}: Neustart {1} liegt vor Updatestart {2}",
                        s.Name,
                        s.RebootScheduledAt.Value.ToString("dd.MM. HH:mm"),
                        s.InstallScheduledAt.Value.ToString("dd.MM. HH:mm")));
                }
            }
            if (timingConflicts.Length > 0)
            {
                if (MessageBox.Show(
                    "Bei folgenden Servern liegt der Neustartzeitpunkt VOR dem Updatestart:\n\n" +
                    timingConflicts.ToString() + "\nTrotzdem fortfahren?",
                    "Zeitplan-Konflikt", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            if (busySkipped.Count > 0)
            {
                AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + busySkipped.Count +
                          " Server übersprungen (bereits in Bearbeitung): " +
                          string.Join(", ", busySkipped.ConvertAll(s => s.Name).ToArray()));
            }

            BeginOperation("Installiere Updates...");
            MarkBusy(selectedServers);

            var cts = new CancellationTokenSource();
            _activeCtsList.Add(cts);

            try
            {
                int successCount   = 0;
                int errorCount     = 0;
                int scheduledCount = 0;

                var progress = new Progress<ServerProgressPair>(pair =>
                {
                    this.InvokeIfRequired(() =>
                    {
                        UpdateServerRow(pair.Server);

                        if (pair.Result != null)
                        {
                            string icon = pair.Result.Success ? "✓" : "✗";
                            string line = "[" + pair.Result.Timestamp.ToString("HH:mm:ss") + "] " +
                                          pair.Result.ServerName.PadRight(20) + " KB" +
                                          (pair.Result.KBArticleId ?? "").PadRight(10) +
                                          " " + icon + "  " + pair.Result.UpdateTitle;
                            if (!pair.Result.Success && !string.IsNullOrEmpty(pair.Result.ErrorMessage))
                                line += "  →  " + pair.Result.ErrorMessage;
                            AppendLog(line);

                            if (pair.Result.Success) successCount++;
                            else errorCount++;
                        }
                        else if (pair.Server.Status == ServerStatus.Scheduled)
                        {
                            scheduledCount++;
                        }
                    });
                });

                await _installerService.InstallUpdatesAsync(selectedServers, progress, (int)_nudParallel.Value, cts.Token);

                this.InvokeIfRequired(() =>
                {
                    string summary = "Installation abgeschlossen: " + successCount + " erfolgreich, " + errorCount + " fehlgeschlagen";
                    if (scheduledCount > 0)
                        summary += ", " + scheduledCount + " zeitgesteuert geplant";
                    summary += ".";
                    SetStatus(summary);
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + summary);
                    MessageBox.Show(summary, "Installation abgeschlossen", MessageBoxButtons.OK,
                        errorCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                });
            }
            catch (OperationCanceledException)
            {
                this.InvokeIfRequired(() => SetStatus("Installation abgebrochen."));
            }
            catch (Exception ex)
            {
                this.InvokeIfRequired(() =>
                {
                    SetStatus("Fehler bei der Installation.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] FEHLER: " + ex.Message);
                });
            }
            finally
            {
                UnmarkBusy(selectedServers);
                _activeCtsList.Remove(cts);
                EndOperation();
            }
        }

        private async Task ScanSingleServerAsync(ServerInfo server)
        {
            if (_busyServers.Contains(server.Name))
            {
                MessageBox.Show(server.Name + " hat bereits einen laufenden Vorgang. Bitte warten.", "Hinweis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BeginOperation("Scanne " + server.Name + " auf Updates...");
            MarkBusy(new[] { server });

            server.Status = ServerStatus.Unknown;
            server.AvailableUpdates.Clear();
            server.ErrorMessage  = null;
            server.StatusMessage = "Ausstehend";
            UpdateServerRow(server);

            var cts = new CancellationTokenSource();
            _activeCtsList.Add(cts);

            try
            {
                var progress = new Progress<ServerInfo>(s =>
                {
                    this.InvokeIfRequired(() => UpdateServerRow(s));
                });

                await _scannerService.ScanServersAsync(new List<ServerInfo> { server }, progress, 1, cts.Token);

                this.InvokeIfRequired(() =>
                {
                    SetStatus(server.Name + ": " + server.AvailableUpdates.Count + " Update(s) gefunden.");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + server.Name + " gescannt: " +
                              server.AvailableUpdates.Count + " Update(s).");
                    RefreshToolbarButtonStates();

                    var selected = _dgvServers.SelectedRows.Count > 0 ? _dgvServers.SelectedRows[0].Tag as ServerInfo : null;
                    if (selected == server)
                        PopulateUpdatesGrid(server);
                });
            }
            catch (OperationCanceledException)
            {
                this.InvokeIfRequired(() => SetStatus("Scan von " + server.Name + " abgebrochen."));
            }
            catch (Exception ex)
            {
                this.InvokeIfRequired(() =>
                {
                    SetStatus("Fehler beim Scan von " + server.Name + ".");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] FEHLER beim Scan von " + server.Name + ": " + ex.Message);
                });
            }
            finally
            {
                UnmarkBusy(new[] { server });
                _activeCtsList.Remove(cts);
                EndOperation();
            }
        }

        private async Task InstallSingleServerAsync(ServerInfo server)
        {
            if (_busyServers.Contains(server.Name))
            {
                MessageBox.Show(server.Name + " hat bereits einen laufenden Vorgang. Bitte warten.", "Hinweis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedUpdates = server.AvailableUpdates.FindAll(u => u.Selected);
            if (selectedUpdates.Count == 0)
            {
                MessageBox.Show("Keine ausgewählten Updates für " + server.Name + " vorhanden.",
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Updates werden auf " + server.Name + " installiert (" + selectedUpdates.Count + " Update(s)).\n\nFortfahren?",
                "Installation bestätigen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            if (server.InstallScheduledAt.HasValue && server.RebootScheduledAt.HasValue
                && server.RebootScheduledAt.Value < server.InstallScheduledAt.Value)
            {
                if (MessageBox.Show(
                    server.Name + ": Der Neustartzeitpunkt liegt VOR dem Updatestart.\n\nTrotzdem fortfahren?",
                    "Zeitplan-Konflikt", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            BeginOperation("Installiere Updates auf " + server.Name + "...");
            MarkBusy(new[] { server });

            var cts = new CancellationTokenSource();
            _activeCtsList.Add(cts);

            try
            {
                int successCount = 0;
                int errorCount    = 0;

                var progress = new Progress<ServerProgressPair>(pair =>
                {
                    this.InvokeIfRequired(() =>
                    {
                        UpdateServerRow(pair.Server);

                        if (pair.Result != null)
                        {
                            string icon = pair.Result.Success ? "✓" : "✗";
                            string line = "[" + pair.Result.Timestamp.ToString("HH:mm:ss") + "] " +
                                          pair.Result.ServerName.PadRight(20) + " KB" +
                                          (pair.Result.KBArticleId ?? "").PadRight(10) +
                                          " " + icon + "  " + pair.Result.UpdateTitle;
                            if (!pair.Result.Success && !string.IsNullOrEmpty(pair.Result.ErrorMessage))
                                line += "  →  " + pair.Result.ErrorMessage;
                            AppendLog(line);

                            if (pair.Result.Success) successCount++;
                            else errorCount++;
                        }
                    });
                });

                await _installerService.InstallUpdatesAsync(
                    new List<ServerInfo> { server }, progress, 1, cts.Token, false);

                this.InvokeIfRequired(() =>
                {
                    string summary = server.Name + ": Installation abgeschlossen (" +
                                      successCount + " erfolgreich, " + errorCount + " fehlgeschlagen).";
                    SetStatus(summary);
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + summary);
                });
            }
            catch (OperationCanceledException)
            {
                this.InvokeIfRequired(() => SetStatus("Installation auf " + server.Name + " abgebrochen."));
            }
            catch (Exception ex)
            {
                this.InvokeIfRequired(() =>
                {
                    SetStatus("Fehler bei Installation auf " + server.Name + ".");
                    AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] FEHLER: " + ex.Message);
                });
            }
            finally
            {
                UnmarkBusy(new[] { server });
                _activeCtsList.Remove(cts);
                EndOperation();
            }
        }

        // =======================================================================
        // Grid Helpers
        // =======================================================================
        private void RefreshServerGrid()
        {
            _suppressCellChange = true;
            _dgvServers.CancelEdit();
            try
            {
                _dgvServers.SuspendLayout();
                _dgvServers.Rows.Clear();

                foreach (var server in _servers)
                {
                    string statusText     = GetStatusText(server);
                    string rebootText     = server.RebootRequired ? "Ja" : "Nein";
                    string updatesText    = server.AvailableUpdates.Count.ToString();

                    int rowIdx = _dgvServers.Rows.Add(
                        server.Selected,
                        server.Name,
                        server.OperatingSystem,
                        statusText,
                        updatesText,
                        rebootText,
                        server.Uptime ?? "—",
                        server.InstallScheduledAt,
                        server.RebootScheduledAt);

                    var row = _dgvServers.Rows[rowIdx];
                    row.Tag = server;

                    if (!server.RebootRequired)
                        row.Cells[8].ReadOnly = true;

                    DarkTheme.StyleDataGridViewRow(row, server.Status);

                    if (server.RebootRequired)
                        row.Cells[5].Style.ForeColor = DarkTheme.RebootWarn;
                }
            }
            finally
            {
                _suppressCellChange = false;
                _dgvServers.ResumeLayout();
            }

            UpdateSelectAllHeaderState();
        }

        private void UpdateServerRow(ServerInfo server)
        {
            var row = _dgvServers.FindServerRow(server.Name);
            if (row == null)
            {
                RefreshServerGrid();
                return;
            }

            _suppressCellChange = true;
            try
            {
                row.Cells[0].Value = server.Selected;
                row.Cells[3].Value = GetStatusText(server);
                row.Cells[4].Value = server.AvailableUpdates.Count.ToString();
                row.Cells[5].Value = server.RebootRequired ? "Ja" : "Nein";
                row.Cells[6].Value = server.Uptime ?? "—";
                row.Cells[7].Value = server.InstallScheduledAt;
                row.Cells[8].Value = server.RebootScheduledAt;

                row.Cells[8].ReadOnly = !server.RebootRequired;

                DarkTheme.StyleDataGridViewRow(row, server.Status);

                row.Cells[5].Style.ForeColor = server.RebootRequired
                    ? DarkTheme.RebootWarn : DarkTheme.Text;
            }
            finally
            {
                _suppressCellChange = false;
            }

            UpdateSelectAllHeaderState();
        }

        // =======================================================================
        // "Alle auswählen" – Checkbox im Spaltenkopf
        // =======================================================================
        private void UpdateSelectAllHeaderState()
        {
            bool allSelected = _servers.Count > 0 && _servers.TrueForAll(s => s.Selected);
            if (allSelected != _selectAllChecked)
            {
                _selectAllChecked = allSelected;
                _dgvServers.InvalidateColumn(0);
            }
        }

        private void DgvServers_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex == 0)
            {
                e.PaintBackground(e.CellBounds, true);
                e.PaintContent(e.CellBounds);

                var state = _selectAllChecked
                    ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                    : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                var size = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                int x = e.CellBounds.Left + (e.CellBounds.Width - size.Width) / 2;
                int y = e.CellBounds.Top  + (e.CellBounds.Height - size.Height) / 2;
                CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(x, y), state);

                e.Handled = true;
            }
        }

        private void DgvServers_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex == 0)
            {
                _selectAllChecked = !_selectAllChecked;
                foreach (var server in _servers)
                    server.Selected = _selectAllChecked;

                RefreshServerGrid();
                _dgvServers.InvalidateColumn(0);
            }
        }

        private static string GetStatusText(ServerInfo server)
        {
            switch (server.Status)
            {
                case ServerStatus.Unknown:    return "Unbekannt";
                case ServerStatus.Online:     return "Online";
                case ServerStatus.Offline:    return "Offline";
                case ServerStatus.Scanning:   return "Scannt...";
                case ServerStatus.Installing: return "Installiert...";
                case ServerStatus.Scheduled:  return "Geplant";
                case ServerStatus.Done:       return "Fertig";
                case ServerStatus.Error:      return "Fehler";
                default:
                    return server.StatusMessage ?? "Unbekannt";
            }
        }

        // =======================================================================
        // Grid Events
        // =======================================================================
        private void DgvServers_SelectionChanged(object sender, EventArgs e)
        {
            if (_dgvServers.SelectedRows.Count == 0)
            {
                _lblUpdateHeader.Text = "Updates für: (kein Server gewählt)";
                _dgvUpdates.Rows.Clear();
                return;
            }

            var row    = _dgvServers.SelectedRows[0];
            var server = row.Tag as ServerInfo;
            if (server == null) return;

            _lblUpdateHeader.Text = "Updates für: " + server.Name;
            PopulateUpdatesGrid(server);
        }

        private void DgvServers_DirtyStateChanged(object sender, EventArgs e)
        {
            if (_dgvServers.CurrentCell is DataGridViewCheckBoxCell)
                _dgvServers.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void DgvServers_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressCellChange) return;
            if (e.RowIndex < 0) return;

            var row    = _dgvServers.Rows[e.RowIndex];
            var server = row.Tag as ServerInfo;
            if (server == null) return;

            if (e.ColumnIndex == 0)
            {
                object v = row.Cells[0].Value;
                server.Selected = (v is bool) ? (bool)v : false;
                UpdateSelectAllHeaderState();
            }
            else if (e.ColumnIndex == 7)
            {
                var val = row.Cells[7].Value;
                if (val is DateTime)
                    server.InstallScheduledAt = (DateTime)val;
                else
                    server.InstallScheduledAt = null;
            }
            else if (e.ColumnIndex == 8)
            {
                var val = row.Cells[8].Value;
                if (val is DateTime)
                    server.RebootScheduledAt = (DateTime)val;
                else
                    server.RebootScheduledAt = null;
            }
        }

        private void DgvServers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row    = _dgvServers.Rows[e.RowIndex];
            var server = row.Tag as ServerInfo;
            if (server == null) return;

            if (e.ColumnIndex == 5)
            {
                string val = e.Value as string;
                e.CellStyle.ForeColor = (val == "Ja") ? DarkTheme.RebootWarn : DarkTheme.Text;
            }
            else if (e.ColumnIndex == 3)
            {
                switch (server.Status)
                {
                    case ServerStatus.Online:
                        e.CellStyle.ForeColor = DarkTheme.Success;
                        break;
                    case ServerStatus.Offline:
                        e.CellStyle.ForeColor = DarkTheme.TextMuted;
                        break;
                    case ServerStatus.Error:
                        e.CellStyle.ForeColor = DarkTheme.Error;
                        break;
                    case ServerStatus.Done:
                        e.CellStyle.ForeColor = DarkTheme.Success;
                        break;
                    case ServerStatus.Scheduled:
                        e.CellStyle.ForeColor = DarkTheme.Accent;
                        break;
                    case ServerStatus.Scanning:
                    case ServerStatus.Installing:
                        e.CellStyle.ForeColor = DarkTheme.Accent;
                        break;
                    default:
                        e.CellStyle.ForeColor = DarkTheme.Text;
                        break;
                }
            }
            else if (e.ColumnIndex == 7)
            {
                if (server.InstallScheduledAt.HasValue && server.RebootScheduledAt.HasValue)
                {
                    double gapMinutes = (server.RebootScheduledAt.Value - server.InstallScheduledAt.Value).TotalMinutes;
                    if (gapMinutes < 60)
                    {
                        e.CellStyle.ForeColor = DarkTheme.Error;
                        e.CellStyle.BackColor = DarkTheme.ErrorTint;
                    }
                }
            }
        }

        private void DgvUpdates_DirtyStateChanged(object sender, EventArgs e)
        {
            if (_dgvUpdates.CurrentCell is DataGridViewCheckBoxCell)
                _dgvUpdates.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void DgvUpdates_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;

            var row    = _dgvUpdates.Rows[e.RowIndex];
            var update = row.Tag as UpdateInfo;
            if (update == null) return;

            object v = row.Cells[0].Value;
            update.Selected = (v is bool) ? (bool)v : false;
        }

        // =======================================================================
        // Update Grid Population
        // =======================================================================
        private void PopulateUpdatesGrid(ServerInfo server)
        {
            _dgvUpdates.SuspendLayout();
            _dgvUpdates.Rows.Clear();

            foreach (var update in server.AvailableUpdates)
            {
                string kbDisplay = string.IsNullOrEmpty(update.KBArticleId) ? "" : "KB" + update.KBArticleId;

                int rowIdx = _dgvUpdates.Rows.Add(
                    update.Selected,
                    kbDisplay,
                    update.Title,
                    update.SizeMB.ToString("F2"),
                    update.RebootRequired ? "Ja" : "Nein",
                    update.Category);

                var row = _dgvUpdates.Rows[rowIdx];
                row.Tag = update;

                if (update.RebootRequired)
                    row.Cells[4].Style.ForeColor = DarkTheme.RebootWarn;
            }

            _dgvUpdates.ResumeLayout();
        }

        // =======================================================================
        // Toolbar button handlers
        // =======================================================================
        private void BtnApplyGlobalInstall_Click(object sender, EventArgs e)
        {
            DateTime globalTime = _dtpGlobalInstall.Value;
            int count = 0;
            foreach (var server in _servers)
            {
                server.InstallScheduledAt = globalTime;
                count++;
            }
            RefreshServerGrid();
            AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Updatestart-Zeit " +
                      globalTime.ToString("dd.MM.yyyy HH:mm") + " für " + count + " Server übernommen.");
        }

        private void BtnApplyGlobalReboot_Click(object sender, EventArgs e)
        {
            DateTime globalTime = _dtpGlobalReboot.Value;
            int count = 0;
            foreach (var server in _servers)
            {
                if (server.RebootRequired)
                {
                    server.RebootScheduledAt = globalTime;
                    count++;
                }
            }
            RefreshServerGrid();
            AppendLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Reboot-Zeit " +
                      globalTime.ToString("dd.MM.yyyy HH:mm") + " für " + count + " Server übernommen.");
        }

        private void BtnDeselectReboot_Click(object sender, EventArgs e)
        {
            foreach (var server in _servers)
            {
                if (server.RebootRequired)
                    server.Selected = false;
            }

            RefreshServerGrid();
            SetStatus("Server mit erforderlichem Reboot abgewählt.");
        }

        private void BtnOpenLog_Click(object sender, EventArgs e)
        {
            try
            {
                string logPath = _logger.LogFilePath;
                if (File.Exists(logPath))
                    Process.Start("explorer.exe", logPath);
                else
                {
                    string dir = Path.GetDirectoryName(logPath);
                    if (Directory.Exists(dir))
                        Process.Start("explorer.exe", dir);
                    else
                        MessageBox.Show("Log-Datei nicht gefunden:\n" + logPath,
                            "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Öffnen des Logs: " + ex.Message,
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =======================================================================
        // Log helpers
        // =======================================================================
        public void AppendLog(string message)
        {
            this.InvokeIfRequired(() =>
            {
                if (_rtbLog.IsDisposed) return;

                _rtbLog.SuspendLayout();

                int start = _rtbLog.TextLength;
                _rtbLog.AppendText(message + Environment.NewLine);
                int end = _rtbLog.TextLength;

                _rtbLog.Select(start, end - start);

                bool isSuccess = message.Contains("✓") || message.ToLower().Contains("erfolgreich");
                bool isError   = message.Contains("✗") || message.ToUpper().Contains("FEHLER") || message.ToUpper().Contains("ERROR");

                if (isSuccess)
                    _rtbLog.SelectionColor = DarkTheme.Success;
                else if (isError)
                    _rtbLog.SelectionColor = DarkTheme.Error;
                else
                    _rtbLog.SelectionColor = DarkTheme.Text;

                _rtbLog.Select(_rtbLog.TextLength, 0);
                _rtbLog.ScrollToCaret();
                _rtbLog.ResumeLayout();
            });
        }

        // =======================================================================
        // Status helpers
        // =======================================================================
        private void SetStatus(string message)
        {
            this.InvokeIfRequired(() =>
            {
                _lblStatusBar.Text  = message;
                _lblToolStatus.Text = message;
            });
        }

        // =======================================================================
        // Parallel-job bookkeeping
        // =======================================================================
        // Reloading the server list while jobs are running would orphan the ServerInfo
        // objects those jobs still reference, so "Server laden" stays blocked until every
        // active operation has finished. Scan/Install stay clickable throughout – concurrent
        // jobs are kept apart per-server via _busyServers, not by a single global lock.
        private void BeginOperation(string message)
        {
            this.InvokeIfRequired(() =>
            {
                _activeOperationCount++;
                _progressBar.Visible    = true;
                _btnLoadServers.Enabled = false;

                if (message != null)
                    SetStatus(message);
            });
        }

        private void EndOperation()
        {
            this.InvokeIfRequired(() =>
            {
                _activeOperationCount = Math.Max(0, _activeOperationCount - 1);
                if (_activeOperationCount == 0)
                {
                    _progressBar.Visible    = false;
                    _btnLoadServers.Enabled = true;
                    SetStatus("Bereit.");
                }
            });
        }

        private void RefreshToolbarButtonStates()
        {
            this.InvokeIfRequired(() =>
            {
                _btnScanUpdates.Enabled    = _servers.Count > 0;
                _btnInstallUpdates.Enabled = _servers.Exists(s => s.AvailableUpdates.Count > 0);
                _btnDeselectReboot.Enabled = _servers.Count > 0;
            });
        }

        private void MarkBusy(IEnumerable<ServerInfo> servers)
        {
            foreach (var s in servers) _busyServers.Add(s.Name);
        }

        private void UnmarkBusy(IEnumerable<ServerInfo> servers)
        {
            this.InvokeIfRequired(() =>
            {
                foreach (var s in servers) _busyServers.Remove(s.Name);
            });
        }

    }
}
