using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace WireFreeCast.Mobile
{
    public partial class MainPage : ContentPage
    {
        private bool isBroadcasting = false;
        private UdpClient udpClient;
        private TcpListener gatekeeperServer; // Security Taala
        private string currentPin = ""; // 🔒 NAYA: PIN store karne ke liye

        public MainPage()
        {
            InitializeComponent();
        }

        private void StartBtn_Clicked(object sender, EventArgs e)
        {
            if (isBroadcasting) return;

            // 1. 🔒 NAYA: Random 4-digit PIN generate karein
            Random rnd = new Random();
            currentPin = rnd.Next(1000, 9999).ToString();

            // 2. 🔒 NAYA: UI Update karein (Screen par PIN show karein)
            PinLabel.Text = $"PIN: {currentPin}";
            PinLabel.IsVisible = true;

            isBroadcasting = true;
            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            StatusLabel.Text = "Radar & Security Active 🟢";
            StatusLabel.TextColor = Colors.Green;

            // 3. UDP Radar shuru karo
            _ = StartUDPBroadcaster();

            // 4. Laptop connection ke liye Security Taala kholo (Port 8889)
            StartGatekeeper();
        }

        private void StopBtn_Clicked(object sender, EventArgs e)
        {
            StopRadar();
        }

        private void StopRadar()
        {
            isBroadcasting = false;
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            StatusLabel.Text = "App is Idle 🛑";
            StatusLabel.TextColor = Colors.Red;

            // 🔒 NAYA: App band hone par PIN hide kar dein
            PinLabel.IsVisible = false;
            currentPin = "";

            try
            {
                if (udpClient != null) { udpClient.Close(); udpClient = null; }
                if (gatekeeperServer != null) { gatekeeperServer.Stop(); gatekeeperServer = null; }
            }
            catch { }
        }

        private void StartGatekeeper()
        {
            try
            {
                gatekeeperServer = new TcpListener(IPAddress.Any, 8889);
                gatekeeperServer.Start();
            }
            catch { }
        }

        private async Task StartUDPBroadcaster()
        {
            try
            {
                udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 8888);

                while (isBroadcasting)
                {
                    string myIp = GetLocalIPAddress();
                    if (myIp != "127.0.0.1")
                    {
                        // 🚨 NAYA LOGIC: Message mein IP ke sath current PIN bhi bheja ja raha hai
                        string message = $"WIREFREECAST_IP:{myIp}:{currentPin}";
                        byte[] bytes = Encoding.UTF8.GetBytes(message);
                        await udpClient.SendAsync(bytes, bytes.Length, endPoint);
                    }
                    await Task.Delay(2000);
                }
            }
            catch { }
        }

        private string GetLocalIPAddress()
        {
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation addr in netInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            return "127.0.0.1";
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRadar();
        }
    }
}