using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;

namespace WireFreeCast.Desktop
{
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

        public Form1()
        {
            InitializeComponent();
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Text = "Finding Device...";
            button1.Enabled = false;

            // 1. Sirf Universal IP Finder chale ga
            string targetIp = await GetPhoneIpAsync();

            if (string.IsNullOrEmpty(targetIp))
            {
                MessageBox.Show("Phone ka IP nahi mila! Hotspot connection check karein.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ResetButton();
                return;
            }

            button1.Text = "Fetching Security PIN...";

            // 🚀 2. UDP ki jagah TCP se Direct PIN fetch! (Firewall Proof)
            string expectedPin = await FetchPinFromGatekeeperAsync(targetIp);

            if (string.IsNullOrEmpty(expectedPin))
            {
                MessageBox.Show("Mobile se connect nahi ho pa raha! Mobile app mein 'Start Radar' daba hua hai? Agar haan, toh app ko ek dafa Stop kar ke Start karein.", "Gatekeeper Blocked", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetButton();
                return;
            }

            // 3. Strict Verification
            string enteredPin = PromptForPin();

            if (enteredPin != expectedPin)
            {
                MessageBox.Show("Galat PIN! Connection reject kar diya gaya hai.", "Security Alert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetButton();
                return;
            }

            button1.Text = $"Unlocked ({targetIp})! Connecting...";

            bool isConnected = await RunAdbCommandAsync($"connect {targetIp}:5555");

            if (!isConnected)
            {
                MessageBox.Show("Connection fail. Wireless Debugging on hai?");
                ResetButton();
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = scrcpyPath,
                    Arguments = $"-s {targetIp}:5555 --window-borderless -b 4M -m 1080 --max-fps 60 --video-codec=h264 -S -w --stay-awake --audio-buffer=50",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ActivateBluetoothAudioSink();

                scrcpyProcess = Process.Start(psi);

                int retries = 0;
                while (scrcpyProcess.MainWindowHandle == IntPtr.Zero && retries < 60)
                {
                    await Task.Delay(100);
                    scrcpyProcess.Refresh();
                    retries++;
                }

                if (scrcpyProcess.MainWindowHandle != IntPtr.Zero)
                {
                    SetParent(scrcpyProcess.MainWindowHandle, pictureBox1.Handle);
                    SetWindowLong(scrcpyProcess.MainWindowHandle, GWL_STYLE, WS_VISIBLE);
                    MoveWindow(scrcpyProcess.MainWindowHandle, 0, 0, pictureBox1.Width, pictureBox1.Height, true);

                    button1.Text = "Connected! (Beast Mode)";
                    _ = MonitorConnectionAsync(targetIp);
                }
                else
                {
                    MessageBox.Show("Engine start nahi ho saka.");
                    ResetButton();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                ResetButton();
            }
        }

        private string PromptForPin()
        {
            Form prompt = new Form() { Width = 320, Height = 160, FormBorderStyle = FormBorderStyle.FixedDialog, Text = "Security Verification", StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false };
            Label textLabel = new Label() { Left = 20, Top = 20, Text = "Mobile app par nazar aane wala 4-digit PIN likhein:", Width = 260 };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 260, MaxLength = 4, Font = new System.Drawing.Font("Segoe UI", 12f) };
            Button confirmation = new Button() { Text = "Verify & Connect", Left = 160, Width = 120, Top = 85, DialogResult = DialogResult.OK };
            prompt.Controls.Add(textLabel); prompt.Controls.Add(textBox); prompt.Controls.Add(confirmation); prompt.AcceptButton = confirmation;
            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }

        private void ActivateBluetoothAudioSink()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%Audio Sink%' OR Caption LIKE '%Bluetooth Audio%'");
                foreach (ManagementObject device in searcher.Get())
                {
                    if (device["Status"].ToString() != "OK") { device.InvokeMethod("Enable", device.GetMethodParameters("Enable"), null); }
                }
            }
            catch { }
        }

        private async Task MonitorConnectionAsync(string ip)
        {
            while (scrcpyProcess != null && !scrcpyProcess.HasExited)
            {
                await Task.Delay(500); // 0.5 sec fast disconnect
                string pin = await FetchPinFromGatekeeperAsync(ip);
                if (string.IsNullOrEmpty(pin))
                {
                    if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill();
                    await RunAdbCommandAsync("disconnect");
                    this.Invoke((MethodInvoker)delegate { ResetButton(); button1.Text = "Disconnected!"; pictureBox1.Image = null; pictureBox1.Refresh(); });
                    break;
                }
            }
        }

        // 🚀 THE MAGIC: TCP GET PIN (No UDP, No Firewall Issues)
        private async Task<string> FetchPinFromGatekeeperAsync(string ip)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(ip, 8889, null, null);
                    var success = await Task.Run(() => result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)));
                    if (success)
                    {
                        client.EndConnect(result);
                        NetworkStream stream = client.GetStream();
                        client.ReceiveTimeout = 1000;
                        byte[] buffer = new byte[1024];
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    }
                }
            }
            catch { }
            return null;
        }

        // 🚀 THE FALLBACK: Jo hamesha IP pakarta hai
        private async Task<string> GetPhoneIpAsync()
        {
            string foundIp = null;
            await Task.Run(() =>
            {
                try
                {
                    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus == OperationalStatus.Up)
                        {
                            foreach (GatewayIPAddressInformation gateway in ni.GetIPProperties().GatewayAddresses)
                            {
                                string gatewayIp = gateway.Address.ToString();
                                if (gatewayIp.Contains(".") && gatewayIp != "0.0.0.0") { foundIp = gatewayIp; break; }
                            }
                        }
                    }
                }
                catch { }
            });
            return foundIp;
        }

        private Task<bool> RunAdbCommandAsync(string arguments)
        {
            return Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (Process p = Process.Start(psi)) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return output.Contains("connected") || output.Contains("already connected"); }
                }
                catch { return false; }
            });
        }

        private void ResetButton() { button1.Text = "Click For Connect"; button1.Enabled = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { }
            RunAdbCommandAsync("kill-server").Wait();
            Environment.Exit(0);
            base.OnFormClosing(e);
        }
    }
}