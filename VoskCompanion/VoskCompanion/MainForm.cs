using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoskCompanion {

    public class MainForm : Form {

        private Button   _listenButton;
        private bool     _listening;
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
            Width = 760;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            MinimumSize = new Size(700, 380);

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
            var clearBtn    = MakeButton("Clear Log",       (s, e) => { _logLines.Clear(); _logBox.Clear(); });
            var restartBtn  = MakeButton("Restart",         (s, e) => { StopServices(); StartServices(); });
            _listenButton   = MakeButton("Start Listening", (s, e) => ToggleListening());
            _listenButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _listenButton.BackColor = Color.FromArgb(60, 140, 70);
            _listenButton.ForeColor = Color.White;
            // _listenButton is added FIRST so that in this RightToLeft flow it sits at the
            // right edge — always visible even if the window is narrow. (Previously it was
            // added last, landing on the left and getting clipped at the default width.)
            buttons.Controls.Add(_listenButton);
            buttons.Controls.Add(restartBtn);
            buttons.Controls.Add(settingsBtn);
            buttons.Controls.Add(clearBtn);
            buttons.Controls.Add(trayBtn);
            buttons.Controls.Add(exitBtn);

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

        // Safety net: guarantees the microphone is released on every actual close
        // path — Exit button, Windows shutdown, log-off, or task-kill-with-close —
        // so an instance can never leave the mic device held for other apps.
        protected override void OnFormClosed(FormClosedEventArgs e) {
            try { StopServices(); } catch { }
            if (_tray != null) _tray.Visible = false;
            base.OnFormClosed(e);
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
                AddLog("Loading model from: " + _settings.ModelPath);

                // Load the model and prepare the engine, but DO NOT open the microphone.
                // The mic is opened only when the user clicks "Start Listening" — so simply
                // launching the app can never touch the audio device.
                _speech = new SpeechEngine();
                _speech.Init(_settings.ModelPath, _settings.MinConfidence);
                _speech.OnPhraseMatched += OnPhraseMatched;
                _speech.OnStatusChanged += s => SetStatus(s, Color.FromArgb(30, 70, 40));
                _speech.OnLog           += AddLog;

                // The UDP link to VAM is harmless (no audio) — start it now.
                _server = new SocketServer();
                _server.OnPhrasesReceived += phrases => _speech?.SetPhrases(phrases);
                _server.OnStatusChanged   += s => { if (!_listening) SetStatus(s + "  —  click Start Listening", Color.FromArgb(60, 60, 80)); };
                _server.OnLog             += AddLog;
                _server.Start(_settings.Port);

                _listening = false;
                UpdateListenButton();
                SetStatus("Ready — click Start Listening to enable the microphone", Color.FromArgb(60, 60, 80));
                AddLog("Model loaded. Microphone is OFF until you click Start Listening.");
            }
            catch (Exception ex) {
                StopServices();
                SetStatus("Startup problem — see log, then click Restart", Color.FromArgb(120, 40, 40));
                AddLog(ex.Message);
            }
            RefreshInfo();
        }

        // ── Listening toggle (the only thing that opens the microphone) ─────

        private void ToggleListening() {
            if (_speech == null) { AddLog("Engine not ready. Click Restart."); return; }
            if (_listening) StopListening();
            else            StartListening();
        }

        private void StartListening() {
            try {
                _speech.Start(); // opens the WASAPI microphone
                _listening = true;
                UpdateListenButton();
                SetStatus($"Listening  (UDP port {_settings.Port})", Color.FromArgb(30, 70, 40));
                AddLog("Microphone ON. Listening.");
            }
            catch (Exception ex) {
                _listening = false;
                UpdateListenButton();
                try { _speech.Stop(); } catch { }
                SetStatus("Microphone unavailable — see log", Color.FromArgb(120, 40, 40));
                AddLog(ex.Message);
            }
        }

        private void StopListening() {
            try { _speech.Stop(); } catch { } // stops + releases the mic
            _listening = false;
            UpdateListenButton();
            SetStatus("Microphone OFF (Stopped). Click Start Listening to resume.", Color.FromArgb(60, 60, 80));
            AddLog("Microphone OFF.");
        }

        private void UpdateListenButton() {
            if (_listenButton == null) return;
            if (_listening) {
                _listenButton.Text = "Stop Listening";
                _listenButton.BackColor = Color.FromArgb(150, 60, 60);
            } else {
                _listenButton.Text = "Start Listening";
                _listenButton.BackColor = Color.FromArgb(60, 140, 70);
            }
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
            _listening = false;
            UpdateListenButton();
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
