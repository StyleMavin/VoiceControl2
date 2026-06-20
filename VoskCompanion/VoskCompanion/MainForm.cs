using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoskCompanion {

    public class MainForm : Form {

        private Label    _statusLabel;
        private Label    _modelLabel;
        private Label    _portLabel;
        private Label    _confidenceLabel;
        private Label    _autoStartLabel;
        private TextBox  _logBox;
        private NotifyIcon _tray;

        private SpeechEngine _speech;
        private SocketServer _server;
        private AppSettings  _settings;
        private bool         _reallyExit;

        private readonly List<string> _logLines = new List<string>();

        public MainForm() {
            _settings = AppSettings.Load();
            BuildUI();
            BuildTray();
            StartServices();
        }

        // ── UI ────────────────────────────────────────────────────────────

        private void BuildUI() {
            Text = "VoskCompanion";
            Width = 560;
            Height = 480;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            MinimumSize = new Size(420, 320);

            // Status banner
            _statusLabel = new Label {
                Dock = DockStyle.Top,
                Height = 44,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "Starting...",
                BackColor = Color.FromArgb(40, 44, 52),
                ForeColor = Color.White
            };

            // Config info panel
            var info = new TableLayoutPanel {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 4,
                Height = 110,
                Padding = new Padding(10, 8, 10, 8)
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _modelLabel      = ValueLabel();
            _portLabel       = ValueLabel();
            _confidenceLabel = ValueLabel();
            _autoStartLabel  = ValueLabel();

            info.Controls.Add(KeyLabel("Vosk Model:"),    0, 0); info.Controls.Add(_modelLabel,      1, 0);
            info.Controls.Add(KeyLabel("UDP Port:"),      0, 1); info.Controls.Add(_portLabel,       1, 1);
            info.Controls.Add(KeyLabel("Min Confidence:"),0, 2); info.Controls.Add(_confidenceLabel, 1, 2);
            info.Controls.Add(KeyLabel("Start w/ Windows:"),0, 3); info.Controls.Add(_autoStartLabel,1, 3);

            // Log
            _logBox = new TextBox {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
                BackColor = Color.FromArgb(24, 26, 31),
                ForeColor = Color.Gainsboro,
                WordWrap = false
            };

            // Buttons
            var buttons = new FlowLayoutPanel {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(6)
            };
            var exitBtn     = MakeButton("Exit",            (s, e) => DoExit());
            var trayBtn     = MakeButton("Minimize to Tray",(s, e) => HideToTray());
            var settingsBtn = MakeButton("Settings...",     (s, e) => ShowSettings());
            var restartBtn  = MakeButton("Restart",         (s, e) => { StopServices(); StartServices(); });
            var clearBtn    = MakeButton("Clear Log",       (s, e) => { _logLines.Clear(); _logBox.Clear(); });
            buttons.Controls.Add(exitBtn);
            buttons.Controls.Add(trayBtn);
            buttons.Controls.Add(settingsBtn);
            buttons.Controls.Add(restartBtn);
            buttons.Controls.Add(clearBtn);

            // Order matters: Fill must be added before docked siblings to layout correctly
            Controls.Add(_logBox);
            Controls.Add(info);
            Controls.Add(_statusLabel);
            Controls.Add(buttons);

            RefreshInfo();
        }

        private static Label KeyLabel(string t) => new Label {
            Text = t, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        private static Label ValueLabel() => new Label {
            Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        private Button MakeButton(string text, EventHandler onClick) {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(4, 4, 4, 4) };
            b.Click += onClick;
            return b;
        }

        private void RefreshInfo() {
            string model = string.IsNullOrWhiteSpace(_settings.ModelPath) ? "(not set)" : _settings.ModelPath;
            _modelLabel.Text      = model;
            _portLabel.Text       = _settings.Port.ToString();
            _confidenceLabel.Text = _settings.MinConfidence.ToString("F2");
            _autoStartLabel.Text  = _settings.AutoStart ? "Yes" : "No";
        }

        // ── Tray ──────────────────────────────────────────────────────────

        private void BuildTray() {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show Window", null, (s, e) => ShowFromTray());
            menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => DoExit());

            _tray = new NotifyIcon {
                Icon = SystemIcons.Application,
                Text = "VoskCompanion",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        private void HideToTray() {
            Hide();
            _tray.ShowBalloonTip(1500, "VoskCompanion", "Still running in the tray. Double-click to reopen.", ToolTipIcon.Info);
        }

        private void ShowFromTray() {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            // X button minimizes to tray instead of quitting, unless Exit was chosen
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        private void DoExit() {
            _reallyExit = true;
            _tray.Visible = false;
            StopServices();
            Application.Exit();
        }

        // ── Services ──────────────────────────────────────────────────────

        private void StartServices() {
            // If no valid model is configured, try to auto-detect a bundled model
            // sitting next to the .exe (e.g. the "vosk-model-small-en-us-0.15" folder
            // shipped in the release zip). Makes a bundled download work out of the box.
            if (string.IsNullOrWhiteSpace(_settings.ModelPath) || !Directory.Exists(_settings.ModelPath)) {
                string detected = TryAutoDetectModel();
                if (detected != null) {
                    _settings.ModelPath = detected;
                    _settings.Save();
                    AddLog("Auto-detected bundled model: " + detected);
                }
            }

            if (string.IsNullOrWhiteSpace(_settings.ModelPath) || !Directory.Exists(_settings.ModelPath)) {
                SetStatus("Model not configured — click Settings", Color.FromArgb(120, 40, 40));
                AddLog("No valid Vosk model path. Open Settings and browse to your model folder.");
                ShowSettings();
                return;
            }

            try {
                SetStatus("Loading model...", Color.FromArgb(70, 70, 30));
                AddLog("Loading model from: " + _settings.ModelPath);

                _speech = new SpeechEngine();
                _speech.Init(_settings.ModelPath, _settings.MinConfidence);
                _speech.OnPhraseMatched += OnPhraseMatched;
                _speech.OnStatusChanged += s => SetStatus(s, Color.FromArgb(30, 70, 40));
                _speech.OnLog           += AddLog;
                _speech.Start();

                _server = new SocketServer();
                _server.OnPhrasesReceived += phrases => _speech.SetPhrases(phrases);
                _server.OnStatusChanged   += s => SetStatus(s, Color.FromArgb(30, 60, 80));
                _server.OnLog             += AddLog;
                _server.Start(_settings.Port);

                SetStatus($"Waiting for VAM  (UDP port {_settings.Port})", Color.FromArgb(30, 60, 80));
                AddLog("Model loaded. Ready.");
            }
            catch (Exception ex) {
                SetStatus("Error — see log", Color.FromArgb(120, 40, 40));
                AddLog("Startup error: " + ex.Message);
            }
            RefreshInfo();
        }

        // Looks for a Vosk model folder next to the executable. A valid model folder
        // contains an "am" subfolder (acoustic model). Returns null if none found.
        private static string TryAutoDetectModel() {
            try {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string dir in Directory.GetDirectories(exeDir, "vosk-model*")) {
                    if (Directory.Exists(Path.Combine(dir, "am")))
                        return dir;
                }
            }
            catch { }
            return null;
        }

        private void StopServices() {
            _speech?.Stop();
            _speech?.Dispose();
            _speech = null;
            _server?.Dispose();
            _server = null;
        }

        private void OnPhraseMatched(string phrase) {
            _server?.SendMatch(phrase);
            if (_settings.LogRecognition) AddLog("Sent MATCH to VAM: \"" + phrase + "\"");
        }

        private void ShowSettings() {
            using (var dlg = new SettingsDialog(_settings)) {
                if (dlg.ShowDialog(this) == DialogResult.OK) {
                    _settings.Save();
                    ApplyAutoStart(_settings.AutoStart);
                    RefreshInfo();
                    StopServices();
                    StartServices();
                }
            }
        }

        // ── Status / log (thread-safe) ────────────────────────────────────

        private void SetStatus(string text, Color back) {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, back))); return; }
            _statusLabel.Text = text;
            _statusLabel.BackColor = back;
            _tray.Text = ("VoskCompanion — " + text).Substring(0, Math.Min(63, ("VoskCompanion — " + text).Length));
        }

        private void AddLog(string message) {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (InvokeRequired) { BeginInvoke(new Action(() => AddLog(message))); return; }
            _logLines.Add(line);
            if (_logLines.Count > 800) _logLines.RemoveAt(0);
            _logBox.AppendText(line + Environment.NewLine);
        }

        // ── Auto-start ────────────────────────────────────────────────────

        private static void ApplyAutoStart(bool enable) {
            const string key = "VoskCompanion";
            string exePath = Assembly.GetExecutingAssembly().Location;
            using (var reg = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)) {
                if (enable) reg.SetValue(key, "\"" + exePath + "\"");
                else        reg.DeleteValue(key, throwOnMissingValue: false);
            }
        }
    }
}
