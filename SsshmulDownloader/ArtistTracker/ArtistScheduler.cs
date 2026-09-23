using System;
using System.Threading.Tasks;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.ArtistTracker
{
    public static class ArtistScheduler
    {
        private static System.Threading.Timer? _timer;
        private static int _intervalHours = 6;
        private static readonly object _lock = new();

        public static void Start()
        {
            _intervalHours = SettingsStore.Current.ScanIntervalHours;
            if (_intervalHours < 1) _intervalHours = 6;

            // Delayed initial scan 30 seconds after startup so app initializes smoothly
            Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
            {
                _ = ArtistScanner.ScanAllArtistsAsync(triggerDownload: true);
            });

            Reschedule(_intervalHours);
        }

        public static void Reschedule(int hours)
        {
            if (hours < 1) hours = 1;
            lock (_lock)
            {
                _intervalHours = hours;
                _timer?.Dispose();

                TimeSpan interval = TimeSpan.FromHours(_intervalHours);
                _timer = new System.Threading.Timer(async _ =>
                {
                    try
                    {
                        await ArtistScanner.ScanAllArtistsAsync(triggerDownload: true);
                    }
                    catch { }
                }, null, interval, interval);
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
