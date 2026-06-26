using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;
using System.Text;

namespace WireFreeCast.Desktop
{
    public class ModernToggle : CheckBox
    {
        public ModernToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(this.Parent.BackColor);

            int radius = this.Height - 1;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(0, 0, radius, radius, 90, 180);
                path.AddArc(this.Width - radius - 1, 0, radius, radius, 270, 180);
                path.CloseFigure();

                Color toggleColor = this.Checked ? Color.FromArgb(40, 160, 80) : Color.FromArgb(200, 50, 50);
                using (var brush = new SolidBrush(toggleColor)) { e.Graphics.FillPath(brush, path); }
            }

            int circleSize = this.Height - 6;
            int circleX = this.Checked ? this.Width - circleSize - 3 : 3;
            using (var brush = new SolidBrush(Color.White)) { e.Graphics.FillEllipse(brush, circleX, 3, circleSize, circleSize); }
        }
    }

    public partial class Form1 : Form
    {
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        const int GWL_STYLE = -16;
        const int WS_VISIBLE = 0x10000000;

        private Process scrcpyProcess;
        private string scrcpyPath = @"C:\scrcpy\scrcpy.exe";
        private string adbPath = @"C:\scrcpy\adb.exe";

        private ComboBox comboRes, comboFps, comboBitrate;
        private ModernToggle toggleScreen;
        private FlowLayoutPanel controlPanel;
        private Label lblScreenToggleText, lblLoading, lblBattery;
        private Button btnSendFast, btnFetchFast, btnPasteText, btnScreenshot, btnRecord, btnStop;
        private NotifyIcon trayIcon;

        private string lastConnectedIp = "";
        private bool isAppActive = false;
        private bool isRestarting = false;
        private string lastNotificationHash = "";
        private bool isRecording = false;
        private string currentRecordPath = "";

        public Form1()
        {
            InitializeComponent();
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            trayIcon = new NotifyIcon() { Visible = true, Icon = SystemIcons.Information, BalloonTipTitle = "WireFreeCast" };
            CreateLoadingOverlay();
            CreatePerformanceControls();
        }

        private void CreateLoadingOverlay()
        {
            lblLoading = new Label() { Text = "Processing...\nPlease Wait", ForeColor = Color.White, BackColor = Color.FromArgb(200, 20, 20, 20), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 16f, FontStyle.Bold), Visible = false };
            pictureBox1.Controls.Add(lblLoading);
        }

        private void CreatePerformanceControls()
        {
            // Panel height increased slightly to fit the new Stop button nicely
            controlPanel = new FlowLayoutPanel() { Dock = DockStyle.Top, Height = 95, BackColor = Color.FromArgb(30, 30, 30), Padding = new Padding(10, 8, 10, 5), WrapContents = true };

            Label lblRes = new Label() { Text = "Res:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) };
            comboRes = new ComboBox() { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            comboRes.Items.AddRange(new string[] { "1080p", "720p", "480p" });
            comboRes.SelectedIndex = 0; comboRes.SelectedIndexChanged += OnSettingsChanged;

            Label lblFps = new Label() { Text = "FPS:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 5, 0, 0) };
            comboFps = new ComboBox() { Width = 55, DropDownStyle = ComboBoxStyle.DropDownList };
            comboFps.Items.AddRange(new string[] { "60", "30" });
            comboFps.SelectedIndex = 0; comboFps.SelectedIndexChanged += OnSettingsChanged;

            Label lblBit = new Label() { Text = "Bitrate:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 5, 0, 0) };
            comboBitrate = new ComboBox() { Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBitrate.Items.AddRange(new string[] { "8M", "4M", "2M", "1M" });
            comboBitrate.SelectedIndex = 1; comboBitrate.SelectedIndexChanged += OnSettingsChanged;

            lblScreenToggleText = new Label() { Text = "Screen: OFF", ForeColor = Color.FromArgb(255, 100, 100), AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(10, 5, 5, 0) };
            toggleScreen = new ModernToggle() { Width = 40, Height = 22, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 0), Checked = false };
            toggleScreen.CheckedChanged += (s, e) => {
                if (toggleScreen.Checked) { lblScreenToggleText.Text = "Screen: ON"; lblScreenToggleText.ForeColor = Color.FromArgb(100, 255, 100); }
                else { lblScreenToggleText.Text = "Screen: OFF"; lblScreenToggleText.ForeColor = Color.FromArgb(255, 100, 100); }
                OnSettingsChanged(s, e);
            };

            lblBattery = new Label() { Text = "🔋 --%", ForeColor = Color.LightGreen, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(15, 4, 0, 0) };

            btnSendFast = new Button() { Text = "📤 Send", ForeColor = Color.White, BackColor = Color.DodgerBlue, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(10, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnSendFast.FlatAppearance.BorderSize = 0; btnSendFast.Click += BtnSendFast_Click;

            btnFetchFast = new Button() { Text = "📥 Get", ForeColor = Color.White, BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(5, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnFetchFast.FlatAppearance.BorderSize = 0; btnFetchFast.Click += BtnFetchFast_Click;

            btnPasteText = new Button() { Text = "📝 Paste", ForeColor = Color.White, BackColor = Color.DarkOrange, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(5, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnPasteText.FlatAppearance.BorderSize = 0; btnPasteText.Click += BtnPasteText_Click;

            btnScreenshot = new Button() { Text = "📸 Shot", ForeColor = Color.White, BackColor = Color.DarkOrchid, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(5, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnScreenshot.FlatAppearance.BorderSize = 0; btnScreenshot.Click += BtnScreenshot_Click;

            btnRecord = new Button() { Text = "🔴 Rec", ForeColor = Color.White, BackColor = Color.DarkRed, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(5, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnRecord.FlatAppearance.BorderSize = 0; btnRecord.Click += BtnRecord_Click;

            // 🚀 NAYA: Instant Stop Button
            btnStop = new Button() { Text = "⏹️ Stop", ForeColor = Color.White, BackColor = Color.Crimson, FlatStyle = FlatStyle.Flat, Width = 70, Height = 26, Margin = new Padding(5, 5, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnStop.FlatAppearance.BorderSize = 0; btnStop.Click += BtnStop_Click;

            controlPanel.Controls.Add(lblRes); controlPanel.Controls.Add(comboRes); controlPanel.Controls.Add(lblFps); controlPanel.Controls.Add(comboFps); controlPanel.Controls.Add(lblBit); controlPanel.Controls.Add(comboBitrate); controlPanel.Controls.Add(lblScreenToggleText); controlPanel.Controls.Add(toggleScreen); controlPanel.Controls.Add(lblBattery);
            controlPanel.Controls.Add(btnSendFast); controlPanel.Controls.Add(btnFetchFast); controlPanel.Controls.Add(btnPasteText); controlPanel.Controls.Add(btnScreenshot); controlPanel.Controls.Add(btnRecord); controlPanel.Controls.Add(btnStop);

            this.Controls.Add(controlPanel);
            if (button1 != null) button1.BringToFront();
            pictureBox1.SendToBack();

            this.AllowDrop = true; this.DragEnter += Form1_DragEnter; this.DragDrop += Form1_DragDrop;
        }

        // 🚀 MASTER DISCONNECT FUNCTION (Instant Kill)
        private void DisconnectNow()
        {
            isAppActive = false;

            // Foran process ko terminate karein bina wait kiye
            try
            {
                if (scrcpyProcess != null && !scrcpyProcess.HasExited)
                {
                    scrcpyProcess.Kill();
                    scrcpyProcess.Dispose();
                }
            }
            catch { }

            // Background mein ADB server ko clean karein
            Task.Run(() => RunCmd("kill-server"));

            // UI ko instant reset karein
            this.Invoke((MethodInvoker)delegate {
                lblLoading.Visible = false;
                ResetButton();
                btnSendFast.Enabled = false;
                btnFetchFast.Enabled = false;
                btnPasteText.Enabled = false;
                btnScreenshot.Enabled = false;
                btnRecord.Enabled = false;
                btnStop.Enabled = false;
                pictureBox1.Image = null; // Screen black out foran
                pictureBox1.Refresh();
                lblBattery.Text = "🔋 --%";
            });
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            DisconnectNow();
            trayIcon.ShowBalloonTip(2000, "Disconnected", "Connection stopped instantly.", ToolTipIcon.Info);
        }

        private void RunCmd(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true };
                using (Process p = Process.Start(psi)) { p.WaitForExit(); }
            }
            catch { }
        }

        private async void BtnScreenshot_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;
            string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"WireFreeCast_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            lblLoading.Text = "Capturing Screenshot..."; lblLoading.Visible = true; lblLoading.BringToFront();
            await Task.Run(() => { RunCmd($"-s {lastConnectedIp}:5555 exec-out screencap -p > \"{savePath}\""); });
            lblLoading.Visible = false;
            if (File.Exists(savePath)) { trayIcon.ShowBalloonTip(3000, "Screenshot Saved", "Image has been saved to your Pictures directory.", ToolTipIcon.Info); }
        }

        private async void BtnRecord_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;
            if (!isRecording)
            {
                currentRecordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), $"WireFreeCast_{DateTime.Now:yyyyMMdd_HHmmss}.mkv");
                isRecording = true; btnRecord.Text = "⏹️ Stop"; btnRecord.BackColor = Color.Crimson;
                await ReloadEngineLiveAsync();
                trayIcon.ShowBalloonTip(3000, "Recording Started", "Screen recording has started. Saved in Videos directory.", ToolTipIcon.Info);
            }
            else
            {
                isRecording = false; btnRecord.Text = "🔴 Rec"; btnRecord.BackColor = Color.DarkRed;
                string savedPath = currentRecordPath; currentRecordPath = "";
                await ReloadEngineLiveAsync();
                Process.Start("explorer.exe", $"/select,\"{savedPath}\"");
            }
        }

        private async void BtnPasteText_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;
            if (Clipboard.ContainsText())
            {
                string escapedText = Clipboard.GetText().Replace(" ", "%s").Replace("\"", "\\\"").Replace("'", "\\'").Replace("&", "\\&").Replace("<", "\\<").Replace(">", "\\>");
                lblLoading.Text = "Pasting Clipboard Data..."; lblLoading.Visible = true; lblLoading.BringToFront();
                await RunAdbCommandAsync($"shell input text \"{escapedText}\"");
                lblLoading.Visible = false;
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }

        private async void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (!isAppActive) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string file in files) await TransferFileWithLogsAsync($"push \"{file}\" \"/sdcard/Download/{Path.GetFileName(file)}\"", "Transferring File...");
        }

        private async void BtnSendFast_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;
            OpenFileDialog ofd = new OpenFileDialog() { Title = "Select File to Transfer" };
            if (ofd.ShowDialog() == DialogResult.OK) await TransferFileWithLogsAsync($"push \"{ofd.FileName}\" \"/sdcard/Download/{Path.GetFileName(ofd.FileName)}\"", "Transferring File...");
        }

        private async void BtnFetchFast_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;
            lblLoading.Text = "Fetching Recent Files..."; lblLoading.Visible = true; lblLoading.BringToFront();
            string output = await RunAdbCommandWithOutputAsync($"shell \"ls -t -p /sdcard/Download/ | grep -v / | head -n 15\"");
            lblLoading.Visible = false;

            if (string.IsNullOrWhiteSpace(output))
            {
                MessageBox.Show("No recent files found in the device's Download folder.", "No Files Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string[] files = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Form fm = new Form() { Width = 380, Height = 400, Text = "Recent Device Files" };
            ListBox lb = new ListBox() { Dock = DockStyle.Fill }; lb.Items.AddRange(files); fm.Controls.Add(lb);
            lb.DoubleClick += async (s, ev) => {
                if (lb.SelectedItem == null) return; string f = lb.SelectedItem.ToString(); fm.Close();
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", f);
                if (await TransferFileWithLogsAsync($"pull \"/sdcard/Download/{f}\" \"{p}\"", "Downloading File...")) Process.Start("explorer.exe", $"/select,\"{p}\"");
            };
            fm.ShowDialog();
        }

        private async Task<bool> TransferFileWithLogsAsync(string args, string loadingText)
        {
            lblLoading.Text = loadingText; lblLoading.Visible = true; lblLoading.BringToFront();
            bool success = await Task.Run(() => { ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true }; using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode == 0; } });
            lblLoading.Visible = false;

            if (!success) { MessageBox.Show("File transfer failed. Please check the device storage.", "Transfer Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return success;
        }

        private Task<string> RunAdbCommandWithOutputAsync(string arguments)
        {
            return Task.Run(() => { try { ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; } } catch { return ""; } });
        }

        private async Task MonitorNotificationsAsync(string ip)
        {
            while (isAppActive)
            {
                await Task.Delay(12000);
                await Task.Run(() => {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = $"-s {ip}:5555 shell dumpsys notification --noredact", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                        using (Process p = Process.Start(psi))
                        {
                            string o = p.StandardOutput.ReadToEnd(); Match t = Regex.Match(o, @"android.title=String \((.*?)\)"); Match tx = Regex.Match(o, @"android.text=String \((.*?)\)");
                            if (t.Success && tx.Success)
                            {
                                string h = t.Groups[1].Value + tx.Groups[1].Value;
                                if (h != lastNotificationHash && !string.IsNullOrWhiteSpace(t.Groups[1].Value)) { lastNotificationHash = h; this.Invoke((MethodInvoker)delegate { trayIcon.ShowBalloonTip(5000, $"📱 {t.Groups[1].Value}", tx.Groups[1].Value, ToolTipIcon.Info); }); }
                            }
                        }
                    }
                    catch { }
                });
            }
        }

        private async Task FetchBatteryLevelAsync(string ip)
        {
            await Task.Run(() => { try { ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = $"-s {ip}:5555 shell dumpsys battery", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); Match m = Regex.Match(o, @"level:\s+(\d+)"); if (m.Success) { this.Invoke((MethodInvoker)delegate { lblBattery.Text = $"🔋 {m.Groups[1].Value}%"; }); } } } catch { } });
        }

        private async void OnSettingsChanged(object sender, EventArgs e) { if (isAppActive && scrcpyProcess != null) { await ReloadEngineLiveAsync(); } }
        private async Task ReloadEngineLiveAsync() { if (isRestarting) return; isRestarting = true; lblLoading.Text = "Applying Settings..."; lblLoading.Visible = true; lblLoading.BringToFront(); try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { } await Task.Delay(250); StartScrcpyEngine(lastConnectedIp); }

        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Text = "Locating Device..."; button1.Enabled = false;
            string targetIp = await GetPhoneIpAsync();

            if (string.IsNullOrEmpty(targetIp))
            {
                MessageBox.Show("Device IP not found! Please ensure both devices are connected to the same Wi-Fi or Hotspot network.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ResetButton(); return;
            }

            button1.Text = "Authenticating...";
            string expectedPin = await FetchPinFromGatekeeperAsync(targetIp);

            if (string.IsNullOrEmpty(expectedPin))
            {
                MessageBox.Show("Connection refused by the device. Please ensure you have opened the WireFreeCast mobile app and tapped 'Start Radar'.", "Gatekeeper Blocked", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetButton(); return;
            }

            if (PromptForPin() != expectedPin)
            {
                MessageBox.Show("Incorrect PIN entered. Authentication failed.", "Security Alert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetButton(); return;
            }

            button1.Text = "Initializing Engine...";
            await RunAdbCommandAsync($"connect {targetIp}:5555");
            lastConnectedIp = targetIp; isAppActive = true;

            // Sab buttons ON karo connect hone pe
            btnSendFast.Enabled = true; btnFetchFast.Enabled = true; btnPasteText.Enabled = true; btnScreenshot.Enabled = true; btnRecord.Enabled = true; btnStop.Enabled = true;

            StartScrcpyEngine(targetIp); button1.Text = "Connected!";
            _ = FetchBatteryLevelAsync(targetIp); _ = MonitorConnectionAsync(targetIp); _ = MonitorNotificationsAsync(targetIp);
        }

        private void StartScrcpyEngine(string ip)
        {
            try
            {
                string r = comboRes.Text == "1080p" ? "1080" : comboRes.Text == "720p" ? "720" : "480";
                string screenMode = toggleScreen.Checked ? "--stay-awake" : "-S --stay-awake";
                string recParam = isRecording ? $"--record \"{currentRecordPath}\"" : "";
                ProcessStartInfo psi = new ProcessStartInfo { FileName = scrcpyPath, Arguments = $"-s {ip}:5555 --window-borderless -b {comboBitrate.Text.ToLower()} -m {r} --max-fps {comboFps.Text} --video-codec=h264 {screenMode} -w --audio-buffer=50 {recParam}", UseShellExecute = false, CreateNoWindow = true };
                scrcpyProcess = Process.Start(psi);
                int ret = 0; while (scrcpyProcess.MainWindowHandle == IntPtr.Zero && ret < 60) { Task.Delay(100).Wait(); scrcpyProcess.Refresh(); ret++; }
                if (scrcpyProcess.MainWindowHandle != IntPtr.Zero) { SetParent(scrcpyProcess.MainWindowHandle, pictureBox1.Handle); SetWindowLong(scrcpyProcess.MainWindowHandle, GWL_STYLE, WS_VISIBLE); MoveWindow(scrcpyProcess.MainWindowHandle, 0, 0, pictureBox1.Width, pictureBox1.Height, true); }
                lblLoading.Visible = false; isRestarting = false;
            }
            catch { lblLoading.Visible = false; isRestarting = false; }
        }

        // 🚀 SUPER FAST 0.5s DISCONNECT DETECTOR
        private async Task MonitorConnectionAsync(string ip)
        {
            int mb = 0;
            while (isAppActive)
            {
                await Task.Delay(500); // 0.5 second ka interval
                if (isRestarting) continue;

                if (string.IsNullOrEmpty(await FetchPinFromGatekeeperAsync(ip)))
                {
                    mb++;
                    if (mb >= 2) // Total 1 second ka waqt (disconnect confirm karne ke liye)
                    {
                        DisconnectNow();
                        break;
                    }
                }
                else mb = 0;
            }
        }

        private async Task<string> FetchPinFromGatekeeperAsync(string ip)
        {
            try { using (TcpClient c = new TcpClient()) { var r = c.BeginConnect(ip, 8889, null, null); if (await Task.Run(() => r.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))) { c.EndConnect(r); NetworkStream s = c.GetStream(); byte[] b = new byte[1024]; int br = await s.ReadAsync(b, 0, b.Length); return Encoding.UTF8.GetString(b, 0, br); } } } catch { }
            return null;
        }

        private async Task<string> GetPhoneIpAsync() { string f = null; await Task.Run(() => { try { foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) { if (ni.OperationalStatus == OperationalStatus.Up) { foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses) { if (g.Address.ToString().Contains(".") && g.Address.ToString() != "0.0.0.0") f = g.Address.ToString(); } } } } catch { } }); return f; }

        private Task<bool> RunAdbCommandAsync(string a) { return Task.Run(() => { try { ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = a, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o.Contains("connected") || o.Contains("already connected"); } } catch { return false; } }); }

        private string PromptForPin()
        {
            Form prompt = new Form() { Width = 320, Height = 160, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "Security Verification", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false };
            Label textLabel = new Label() { Left = 20, Top = 20, Text = "Please enter the 4-digit PIN from the mobile app:", Width = 260 };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 260, MaxLength = 4, Font = new Font("Segoe UI", 12f) };
            Button confirmation = new Button() { Text = "Verify & Connect", Left = 150, Width = 130, Top = 85, DialogResult = DialogResult.OK };
            prompt.Controls.Add(textLabel); prompt.Controls.Add(textBox); prompt.Controls.Add(confirmation); prompt.AcceptButton = confirmation;
            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }

        private void ResetButton() { button1.Text = "Click To Connect"; button1.Enabled = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisconnectNow();
            trayIcon.Dispose();
            base.OnFormClosing(e);
        }
    }
}