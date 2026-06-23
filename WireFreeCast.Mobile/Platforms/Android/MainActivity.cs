using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;
using Android.OS;
using System.IO;

namespace WireFreeCast.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public static MainActivity Instance { get; private set; }
        private MediaProjectionManager _projectionManager;
        public Stream CurrentNetworkStream { get; set; }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Instance = this;
        }

        public void StartScreenCapture(Stream stream)
        {
            CurrentNetworkStream = stream;
            _projectionManager = (MediaProjectionManager)GetSystemService(Context.MediaProjectionService);
            StartActivityForResult(_projectionManager.CreateScreenCaptureIntent(), 1000);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == 1000 && resultCode == Result.Ok)
            {
                // Naya Code: Data ko directly proper Android Service mein bhejo
                ScreenCaptureService.CaptureIntent = data;
                ScreenCaptureService.NetworkStream = CurrentNetworkStream;

                Intent serviceIntent = new Intent(this, typeof(ScreenCaptureService));
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    StartForegroundService(serviceIntent);
                }
                else
                {
                    StartService(serviceIntent);
                }
            }
        }
    }
}