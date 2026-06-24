using System;
using System.Net;
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
        private TcpListener gatekeeperServer;
        private string currentPin = "";

        public MainPage()
        {
            InitializeComponent();
        }

        private void StartBtn_Clicked(object sender, EventArgs e)
        {
            if (isBroadcasting) return;

            Random rnd = new Random();
            currentPin = rnd.Next(1000, 9999).ToString();
            PinLabel.Text = $"PIN: {currentPin}";
            PinLabel.IsVisible = true;

            isBroadcasting = true;
            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            StatusLabel.Text = "Security Gatekeeper Active 🟢";
            StatusLabel.TextColor = Colors.Green;

            // 🚀 NAYA: UDP Khatam! Ab sirf TCP Gatekeeper chalega
            _ = StartTCPGatekeeper();
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

            PinLabel.IsVisible = false;
            currentPin = "";

            try
            {
                if (gatekeeperServer != null) { gatekeeperServer.Stop(); gatekeeperServer = null; }
            }
            catch { }
        }

        // 🚀 THE MAGIC: TCP GATEKEEPER JO PIN BHEJEGA
        private async Task StartTCPGatekeeper()
        {
            try
            {
                gatekeeperServer = new TcpListener(IPAddress.Any, 8889);
                gatekeeperServer.Start();

                while (isBroadcasting)
                {
                    TcpClient client = await gatekeeperServer.AcceptTcpClientAsync();
                    NetworkStream stream = client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(currentPin);
                    await stream.WriteAsync(data, 0, data.Length);
                    client.Close();
                }
            }
            catch { }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRadar();
        }
    }
}