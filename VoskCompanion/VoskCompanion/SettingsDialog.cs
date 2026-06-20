using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VoskCompanion {

    public class SettingsDialog : Form {

        private readonly AppSettings _settings;
        private TextBox  _portBox;
        private TextBox  _modelBox;
        private TrackBar _confidenceBar;
        private Label    _confidenceLabel;
        private CheckBox _autoStartBox;
        private CheckBox _logBox;

        public SettingsDialog(AppSettings settings) {
            _settings = settings;
            Text = "VoskCompanion Settings";
            Width = 480;
            Height = 310;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;

            BuildUI();
            LoadValues();
        }

        private void BuildUI() {
            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            // Model path
            layout.Controls.Add(Label("Vosk Model Folder"), 0, 0);
            _modelBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            layout.Controls.Add(_modelBox, 1, 0);
            var browseBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            browseBtn.Click += OnBrowse;
            layout.Controls.Add(browseBtn, 2, 0);

            // Port
            layout.Controls.Add(Label("TCP Port"), 0, 1);
            _portBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_portBox, 1, 1);
            layout.Controls.Add(new Label { Text = "(VAM must match)", Dock = DockStyle.Fill, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8) }, 2, 1);

            // Confidence
            layout.Controls.Add(Label("Min Confidence"), 0, 2);
            _confidenceBar = new TrackBar {
                Minimum = 0, Maximum = 100, TickFrequency = 10,
                Dock = DockStyle.Fill
            };
            _confidenceBar.ValueChanged += (s, e) =>
                _confidenceLabel.Text = (_confidenceBar.Value / 100f).ToString("F2");
            layout.Controls.Add(_confidenceBar, 1, 2);
            _confidenceLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(_confidenceLabel, 2, 2);

            // Auto-start
            layout.Controls.Add(Label(""), 0, 3);
            _autoStartBox = new CheckBox { Text = "Start with Windows", Dock = DockStyle.Fill };
            layout.SetColumnSpan(_autoStartBox, 2);
            layout.Controls.Add(_autoStartBox, 1, 3);

            // Log recognition
            layout.Controls.Add(Label(""), 0, 4);
            _logBox = new CheckBox { Text = "Log recognized phrases", Dock = DockStyle.Fill };
            layout.SetColumnSpan(_logBox, 2);
            layout.Controls.Add(_logBox, 1, 4);

            // Model download hint
            var hint = new Label {
                Text = "Download models from alphacephei.com/vosk/models  (vosk-model-small-en-us recommended)",
                Dock = DockStyle.Fill,
                ForeColor = Color.DodgerBlue,
                Font = new Font(Font.FontFamily, 8),
                AutoSize = false
            };
            layout.SetColumnSpan(hint, 3);
            layout.Controls.Add(hint, 0, 5);

            // Buttons
            var btnPanel = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            var okBtn     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Width = 75 };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            okBtn.Click += OnOk;
            btnPanel.Controls.Add(cancelBtn);
            btnPanel.Controls.Add(okBtn);
            layout.SetColumnSpan(btnPanel, 3);
            layout.Controls.Add(btnPanel, 0, 6);

            Controls.Add(layout);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;
        }

        private void LoadValues() {
            _modelBox.Text        = _settings.ModelPath;
            _portBox.Text         = _settings.Port.ToString();
            _confidenceBar.Value  = (int)(_settings.MinConfidence * 100);
            _autoStartBox.Checked = _settings.AutoStart;
            _logBox.Checked       = _settings.LogRecognition;
        }

        private void OnBrowse(object sender, EventArgs e) {
            using (var dlg = new FolderBrowserDialog {
                Description = "Select Vosk model folder (the folder containing 'am', 'conf', 'graph' subfolders)",
                SelectedPath = _modelBox.Text
            }) {
                if (dlg.ShowDialog() == DialogResult.OK)
                    _modelBox.Text = dlg.SelectedPath;
            }
        }

        private void OnOk(object sender, EventArgs e) {
            if (!int.TryParse(_portBox.Text, out int port) || port < 1024 || port > 65535) {
                MessageBox.Show("Port must be a number between 1024 and 65535.", "Invalid Port",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (!string.IsNullOrWhiteSpace(_modelBox.Text) && !Directory.Exists(_modelBox.Text)) {
                MessageBox.Show("Model folder does not exist.", "Invalid Path",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            _settings.Port           = port;
            _settings.ModelPath      = _modelBox.Text;
            _settings.MinConfidence  = _confidenceBar.Value / 100f;
            _settings.AutoStart      = _autoStartBox.Checked;
            _settings.LogRecognition = _logBox.Checked;
        }

        private static Label Label(string text) =>
            new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    }
}
