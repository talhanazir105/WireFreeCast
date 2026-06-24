using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;

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
        private Button btnSendFast, btnFetchFast; // 🚀 NAYA: Automated Buttons
        private NotifyIcon trayIcon;

        private string lastConnectedIp = "";
        private bool isAppActive = false;
        private bool isRestarting = false;
        private string lastNotificationHash = "";

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
            controlPanel = new FlowLayoutPanel() { Dock = DockStyle.Top, Height = 85, BackColor = Color.FromArgb(30, 30, 30), Padding = new Padding(10, 8, 10, 5), WrapContents = true };

            Label lblRes = new Label() { Text = "Res:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) };
            comboRes = new ComboBox() { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            comboRes.Items.AddRange(new string[] { "1080p", "720p", "480p" });
            comboRes.SelectedIndex = 0;
            comboRes.SelectedIndexChanged += OnSettingsChanged;

            Label lblFps = new Label() { Text = "FPS:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 5, 0, 0) };
            comboFps = new ComboBox() { Width = 55, DropDownStyle = ComboBoxStyle.DropDownList };
            comboFps.Items.AddRange(new string[] { "60", "30" });
            comboFps.SelectedIndex = 0;
            comboFps.SelectedIndexChanged += OnSettingsChanged;

            Label lblBit = new Label() { Text = "Bitrate:", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 5, 0, 0) };
            comboBitrate = new ComboBox() { Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBitrate.Items.AddRange(new string[] { "8M", "4M", "2M", "1M" });
            comboBitrate.SelectedIndex = 1;
            comboBitrate.SelectedIndexChanged += OnSettingsChanged;

            lblScreenToggleText = new Label() { Text = "Screen: OFF", ForeColor = Color.FromArgb(255, 100, 100), AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(10, 5, 5, 0) };
            toggleScreen = new ModernToggle() { Width = 40, Height = 22, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 0), Checked = false };
            toggleScreen.CheckedChanged += (s, e) =>
            {
                if (toggleScreen.Checked) { lblScreenToggleText.Text = "Screen: ON"; lblScreenToggleText.ForeColor = Color.FromArgb(100, 255, 100); }
                else { lblScreenToggleText.Text = "Screen: OFF"; lblScreenToggleText.ForeColor = Color.FromArgb(255, 100, 100); }
                OnSettingsChanged(s, e);
            };

            // 🚀 NAYA: Automated Airdrop Style Buttons
            btnSendFast = new Button() { Text = "📤 Send to Phone", ForeColor = Color.White, BackColor = Color.DodgerBlue, FlatStyle = FlatStyle.Flat, Width = 110, Height = 26, Margin = new Padding(15, 1, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnSendFast.FlatAppearance.BorderSize = 0;
            btnSendFast.Click += BtnSendFast_Click;

            btnFetchFast = new Button() { Text = "📥 Fetch Recent", ForeColor = Color.White, BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat, Width = 110, Height = 26, Margin = new Padding(5, 1, 0, 0), Cursor = Cursors.Hand, Enabled = false };
            btnFetchFast.FlatAppearance.BorderSize = 0;
            btnFetchFast.Click += BtnFetchFast_Click;

            lblBattery = new Label() { Text = "🔋 --%", ForeColor = Color.LightGreen, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(10, 4, 0, 0) };

            controlPanel.Controls.Add(lblRes); controlPanel.Controls.Add(comboRes); controlPanel.Controls.Add(lblFps); controlPanel.Controls.Add(comboFps); controlPanel.Controls.Add(lblBit); controlPanel.Controls.Add(comboBitrate); controlPanel.Controls.Add(lblScreenToggleText); controlPanel.Controls.Add(toggleScreen); controlPanel.Controls.Add(btnSendFast); controlPanel.Controls.Add(btnFetchFast); controlPanel.Controls.Add(lblBattery);

            this.Controls.Add(controlPanel);
            if (button1 != null) button1.BringToFront();
            pictureBox1.SendToBack();
        }

        // 🚀 FULLY AUTOMATED SEND (No Paths, No Bullshit)
        private async void BtnSendFast_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;

            OpenFileDialog ofd = new OpenFileDialog() { Title = "Select File to Send to Phone's Download Folder" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                string filePath = ofd.FileName;
                string fileName = Path.GetFileName(filePath);

                if (fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    await TransferFileWithLogsAsync($"install \"{filePath}\"", "Installing APK...");
                }
                else
                {
                    // Seedha Download folder mein phekta hai
                    await TransferFileWithLogsAsync($"push \"{filePath}\" \"/sdcard/Download/{fileName}\"", "Sending File...");
                }
            }
        }

        // 🚀 FULLY AUTOMATED RECEIVE (Only shows newest files, auto saves to PC Downloads)
        private async void BtnFetchFast_Click(object sender, EventArgs e)
        {
            if (!isAppActive) return;

            lblLoading.Text = "Scanning Recent Files...";
            lblLoading.Visible = true; lblLoading.BringToFront();

            // Phone ke Download folder se aakhri 15 nayi files utha raha hai (Folders ignore kar ke)
            string command = $"shell \"ls -t -p /sdcard/Download/ | grep -v / | head -n 15\"";
            string output = await RunAdbCommandWithOutputAsync(command);

            lblLoading.Visible = false;

            if (string.IsNullOrWhiteSpace(output))
            {
                MessageBox.Show("Phone ke Download folder mein koi nai file nahi mili.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string[] files = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Sleek Dark Popup
            Form fm = new Form() { Width = 400, Height = 450, Text = "📥 Recent Files in Phone", StartPosition = FormStartPosition.CenterScreen, ShowIcon = false, BackColor = Color.FromArgb(30, 30, 30) };
            Label header = new Label() { Text = "Click a file to auto-download to PC:", ForeColor = Color.White, Dock = DockStyle.Top, Padding = new Padding(10), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            ListBox listBox = new ListBox() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f), BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White, ItemHeight = 25 };
            listBox.Items.AddRange(files);

            fm.Controls.Add(listBox);
            fm.Controls.Add(header);

            listBox.DoubleClick += async (s, ev) =>
            {
                if (listBox.SelectedItem == null) return;
                string selectedFile = listBox.SelectedItem.ToString();
                fm.Close(); // Popup band karo

                // Auto Save Location: PC ka default 'Downloads' folder
                string pcDownloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string savePath = Path.Combine(pcDownloadsFolder, selectedFile);

                bool success = await TransferFileWithLogsAsync($"pull \"/sdcard/Download/{selectedFile}\" \"{savePath}\"", "Fetching File...");

                if (success)
                {
                    // 🚀 MAGIC: File aate hi PC ka folder auto-open ho jayega aur file highlight hogi!
                    Process.Start("explorer.exe", $"/select,\"{savePath}\"");
                }
            };

            fm.ShowDialog();
        }

        // 🚀 MASTER TRANSFER ENGINE (With Error Detection)
        private async Task<bool> TransferFileWithLogsAsync(string args, string loadingText)
        {
            lblLoading.Text = loadingText + "\nPlease wait...";
            lblLoading.Visible = true; lblLoading.BringToFront();

            string errorLog = "";
            bool success = await Task.Run(() =>
            {
                ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    errorLog = p.StandardError.ReadToEnd(); // Agar fail hua toh wajah record karo
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            });

            lblLoading.Visible = false;

            if (success)
            {
                MessageBox.Show("Done! File transferred successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            else
            {
                MessageBox.Show($"Transfer Failed!\nReason: {errorLog}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private Task<string> RunAdbCommandWithOutputAsync(string arguments)
        {
            return Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (Process p = Process.Start(psi)) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return output; }
                }
                catch { return ""; }
            });
        }

        // 🔔 NOTIFICATIONS SYNC
        private async Task MonitorNotificationsAsync(string ip)
        {
            while (isAppActive)
            {
                await Task.Delay(10000);

                await Task.Run(() =>
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = $"-s {ip}:5555 shell dumpsys notification --noredact", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                        using (Process p = Process.Start(psi))
                        {
                            string output = p.StandardOutput.ReadToEnd();
                            Match titleMatch = Regex.Match(output, @"android.title=String \((.*?)\)");
                            Match textMatch = Regex.Match(output, @"android.text=String \((.*?)\)");

                            if (titleMatch.Success && textMatch.Success)
                            {
                                string title = titleMatch.Groups[1].Value;
                                string text = textMatch.Groups[1].Value;
                                string currentHash = title + text;

                                if (currentHash != lastNotificationHash && !string.IsNullOrWhiteSpace(title))
                                {
                                    lastNotificationHash = currentHash;
                                    this.Invoke((MethodInvoker)delegate { trayIcon.ShowBalloonTip(5000, $"📱 {title}", text, ToolTipIcon.Info); });
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
        }

        // 🔋 BATTERY MONITOR
        private async Task FetchBatteryLevelAsync(string ip)
        {
            await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = $"-s {ip}:5555 shell dumpsys battery", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (Process p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        Match match = Regex.Match(output, @"level:\s+(\d+)");
                        if (match.Success) { this.Invoke((MethodInvoker)delegate { lblBattery.Text = $"🔋 {match.Groups[1].Value}%"; }); }
                    }
                }
                catch { }
            });
        }

        private async void OnSettingsChanged(object sender, EventArgs e)
        {
            if (isAppActive && scrcpyProcess != null) { await ReloadEngineLiveAsync(); }
        }

        private async Task ReloadEngineLiveAsync()
        {
            if (isRestarting) return;
            isRestarting = true;
            lblLoading.Text = "Applying Settings...\nPlease Wait"; lblLoading.Visible = true; lblLoading.BringToFront();
            try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { }
            await Task.Delay(250);
            StartScrcpyEngine(lastConnectedIp);
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Text = "Finding Device..."; button1.Enabled = false;
            string targetIp = await GetPhoneIpAsync();
            if (string.IsNullOrEmpty(targetIp)) { MessageBox.Show("Phone ka IP nahi mila!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); ResetButton(); return; }

            button1.Text = "Fetching PIN...";
            string expectedPin = await FetchPinFromGatekeeperAsync(targetIp);
            if (string.IsNullOrEmpty(expectedPin)) { MessageBox.Show("Gatekeeper Blocked!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); ResetButton(); return; }

            string enteredPin = PromptForPin();
            if (enteredPin != expectedPin) { MessageBox.Show("Galat PIN!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); ResetButton(); return; }

            button1.Text = $"Unlocked! Connecting...";
            bool isConnected = await RunAdbCommandAsync($"connect {targetIp}:5555");
            if (!isConnected) { MessageBox.Show("Connection fail."); ResetButton(); return; }

            lastConnectedIp = targetIp; isAppActive = true;
            btnSendFast.Enabled = true; btnFetchFast.Enabled = true;

            StartScrcpyEngine(targetIp);
            button1.Text = "Connected!";

            _ = FetchBatteryLevelAsync(targetIp);
            _ = MonitorConnectionAsync(targetIp);
            _ = MonitorNotificationsAsync(targetIp);
        }

        private async void StartScrcpyEngine(string ip)
        {
            try
            {
                string resParam = comboRes.Text == "1080p" ? "1080" : comboRes.Text == "720p" ? "720" : "480";
                string fpsParam = comboFps.Text; string bitrateParam = comboBitrate.Text.ToLower();
                string screenMode = toggleScreen.Checked ? "--stay-awake" : "-S --stay-awake";

                ProcessStartInfo psi = new ProcessStartInfo { FileName = scrcpyPath, Arguments = $"-s {ip}:5555 --window-borderless -b {bitrateParam} -m {resParam} --max-fps {fpsParam} --video-codec=h264 {screenMode} -w --audio-buffer=50", UseShellExecute = false, CreateNoWindow = true };
                ActivateBluetoothAudioSink(); scrcpyProcess = Process.Start(psi);

                int retries = 0;
                while (scrcpyProcess.MainWindowHandle == IntPtr.Zero && retries < 60) { await Task.Delay(100); scrcpyProcess.Refresh(); retries++; }

                if (scrcpyProcess.MainWindowHandle != IntPtr.Zero)
                {
                    SetParent(scrcpyProcess.MainWindowHandle, pictureBox1.Handle);
                    SetWindowLong(scrcpyProcess.MainWindowHandle, GWL_STYLE, WS_VISIBLE);
                    MoveWindow(scrcpyProcess.MainWindowHandle, 0, 0, pictureBox1.Width, pictureBox1.Height, true);
                    lblLoading.Visible = false; isRestarting = false;
                }
                else { lblLoading.Visible = false; isRestarting = false; }
            }
            catch (Exception ex) { lblLoading.Visible = false; isRestarting = false; MessageBox.Show("Error: " + ex.Message); }
        }

        private string PromptForPin()
        {
            Form prompt = new Form() { Width = 320, Height = 160, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "Security", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false };
            Label textLabel = new Label() { Left = 20, Top = 20, Text = "Mobile app ka 4-digit PIN likhein:", Width = 260 };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 260, MaxLength = 4, Font = new Font("Segoe UI", 12f) };
            Button confirmation = new Button() { Text = "Verify", Left = 160, Width = 120, Top = 85, DialogResult = DialogResult.OK };
            prompt.Controls.Add(textLabel); prompt.Controls.Add(textBox); prompt.Controls.Add(confirmation); prompt.AcceptButton = confirmation;
            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }

        private void ActivateBluetoothAudioSink()
        {
            try { ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%Audio Sink%' OR Caption LIKE '%Bluetooth Audio%'"); foreach (ManagementObject device in searcher.Get()) { if (device["Status"].ToString() != "OK") { device.InvokeMethod("Enable", device.GetMethodParameters("Enable"), null); } } } catch { }
        }

        private async Task MonitorConnectionAsync(string ip)
        {
            int missedBeats = 0;
            while (isAppActive)
            {
                await Task.Delay(1500);
                if (isRestarting) continue;

                string pin = await FetchPinFromGatekeeperAsync(ip);
                if (string.IsNullOrEmpty(pin) && !isRestarting)
                {
                    missedBeats++;
                    if (missedBeats >= 3)
                    {
                        isAppActive = false;
                        try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { }
                        await RunAdbCommandAsync("disconnect");
                        this.Invoke((MethodInvoker)delegate { ResetButton(); button1.Text = "Disconnected!"; pictureBox1.Image = null; pictureBox1.Refresh(); btnSendFast.Enabled = false; btnFetchFast.Enabled = false; lblBattery.Text = "🔋 --%"; });
                        break;
                    }
                }
                else { missedBeats = 0; }
            }
        }

        private async Task<string> FetchPinFromGatekeeperAsync(string ip)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(ip, 8889, null, null);
                    var success = await Task.Run(() => result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)));
                    if (success) { client.EndConnect(result); NetworkStream stream = client.GetStream(); client.ReceiveTimeout = 1000; byte[] buffer = new byte[1024]; int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length); return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead); }
                }
            }
            catch { }
            return null;
        }

        private async Task<string> GetPhoneIpAsync()
        {
            string foundIp = null;
            await Task.Run(() => { try { foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) { if (ni.OperationalStatus == OperationalStatus.Up) { foreach (GatewayIPAddressInformation gateway in ni.GetIPProperties().GatewayAddresses) { string gatewayIp = gateway.Address.ToString(); if (gatewayIp.Contains(".") && gatewayIp != "0.0.0.0") { foundIp = gatewayIp; break; } } } } } catch { } });
            return foundIp;
        }

        private Task<bool> RunAdbCommandAsync(string arguments)
        {
            return Task.Run(() => { try { ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; using (Process p = Process.Start(psi)) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return output.Contains("connected") || output.Contains("already connected"); } } catch { return false; } });
        }

        private void ResetButton() { button1.Text = "Click For Connect"; button1.Enabled = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isAppActive = false;
            trayIcon.Dispose();
            try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { }
            RunAdbCommandAsync("kill-server").Wait();
            Environment.Exit(0);
            base.OnFormClosing(e);
        }
    }
}