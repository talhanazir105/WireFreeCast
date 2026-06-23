using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using System;
using System.IO;
using Stream = System.IO.Stream;

namespace WireFreeCast.Mobile
{
    [Service(ForegroundServiceType = ForegroundService.TypeMediaProjection)]
    public class ScreenCaptureService : Service
    {
        public static Intent CaptureIntent { get; set; }
        public static Stream NetworkStream { get; set; }

        private MediaProjection _mediaProjection;
        private VirtualDisplay _virtualDisplay;
        private ImageReader _imageReader;

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            // 1. Oopar Notification Dikhana (Security Bypass karne ke liye)
            var channelId = "ScreenStreamChannel";
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(channelId, "Screen Capture", NotificationImportance.Low);
                var manager = (NotificationManager)GetSystemService(NotificationService);
                manager.CreateNotificationChannel(channel);
            }

            var notification = new Notification.Builder(this, channelId)
                .SetContentTitle("WireFreeCast")
                .SetContentText("Streaming screen to laptop...")
                .SetSmallIcon(Android.Resource.Drawable.IcMenuCamera)
                .Build();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(1001, notification, ForegroundService.TypeMediaProjection);
            }
            else
            {
                StartForeground(1001, notification);
            }

            // 2. Asli Screen Capture Shuru Karna
            var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService);
            _mediaProjection = projectionManager.GetMediaProjection((int)Result.Ok, CaptureIntent);

            var metrics = Resources.DisplayMetrics;
            int width = 720;
            int height = 1280;
            int density = (int)metrics.DensityDpi;

            _imageReader = ImageReader.NewInstance(width, height, (ImageFormatType)Android.Graphics.Format.Rgba8888, 2);
            _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(NetworkStream), null);

            _virtualDisplay = _mediaProjection.CreateVirtualDisplay(
                "WireFreeCastScreen", width, height, density,
                (DisplayFlags)VirtualDisplayFlags.AutoMirror,
                _imageReader.Surface, null, null);

            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            _virtualDisplay?.Release();
            _imageReader?.Close();
            _mediaProjection?.Stop();
        }
    }

    public class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private Stream _networkStream;

        // UPGRADE 1: Yeh hai wo Traffic Warden (Lock Object) jo frames ko takrane se bachayega
        private static readonly object _streamLock = new object();

        public ImageAvailableListener(Stream networkStream)
        {
            _networkStream = networkStream;
        }

        public void OnImageAvailable(ImageReader reader)
        {
            try
            {
                using (var image = reader.AcquireLatestImage())
                {
                    if (image == null) return;

                    var planes = image.GetPlanes();
                    var buffer = planes[0].Buffer;
                    int pixelStride = planes[0].PixelStride;
                    int rowStride = planes[0].RowStride;
                    int rowPadding = rowStride - pixelStride * image.Width;

                    using (Bitmap bitmap = Bitmap.CreateBitmap(image.Width + rowPadding / pixelStride, image.Height, Bitmap.Config.Argb8888))
                    {
                        bitmap.CopyPixelsFromBuffer(buffer);

                        using (MemoryStream ms = new MemoryStream())
                        {
                            // UPGRADE 2: Speed barhane ke liye Quality 60 se 40 kar di hai
                            bitmap.Compress(Bitmap.CompressFormat.Jpeg, 40, ms);
                            byte[] frameData = ms.ToArray();
                            byte[] lengthData = BitConverter.GetBytes(frameData.Length);

                            // UPGRADE 3: Lock System (Data collision fix)
                            lock (_streamLock)
                            {
                                // Ab ek waqt mein sirf ek frame network pipe mein jayega
                                _networkStream.Write(lengthData, 0, 4);
                                _networkStream.Write(frameData, 0, frameData.Length);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors to keep stream running
            }
        }
    }
}