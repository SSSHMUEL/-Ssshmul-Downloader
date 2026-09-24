using System;
using System.IO;
using SsshmulDownloader.ArtistTracker;

namespace SsshmulDownloader.Storage
{
    public static class PathUtils
    {
        public const string AppName = "Ssshmul Downloader";

        public static string GetAppDataDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, AppName);
            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
            }
            return appDir;
        }

        public static string GetBinDirectory()
        {
            // 1. Check local bin_tools folder alongside the executable
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localBin = Path.Combine(baseDir, "bin_tools");
            if (Directory.Exists(localBin) && File.Exists(Path.Combine(localBin, "yt-dlp.exe")))
            {
                return localBin;
            }

            // 2. Check AppData bin folder
            string appDataBin = Path.Combine(GetAppDataDirectory(), "bin");
            if (!Directory.Exists(appDataBin))
            {
                Directory.CreateDirectory(appDataBin);
            }
            return appDataBin;
        }

        public static string GetWebDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string webDir = Path.Combine(baseDir, "web");
            if (Directory.Exists(webDir))
            {
                return webDir;
            }

            string devWebDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\web"));
            if (Directory.Exists(devWebDir))
            {
                return devWebDir;
            }

            return baseDir;
        }

        public static string GetExtensionDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Check parent root project directory (development environment)
            string devExtDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\extension"));
            if (Directory.Exists(devExtDir))
            {
                return devExtDir;
            }

            // 2. Check local application directory (release / installed environment)
            string extDir = Path.Combine(baseDir, "extension");
            if (Directory.Exists(extDir))
            {
                return extDir;
            }

            return extDir;
        }

        public static string GetArtistsFilePath()
        {
            return Path.Combine(GetAppDataDirectory(), "artists.json");
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetAppDataDirectory(), "settings.json");
        }

        public static string GetLogFilePath()
        {
            return Path.Combine(GetAppDataDirectory(), "app.log");
        }

        public static string GetSavedCookiesFilePath()
        {
            return Path.Combine(GetAppDataDirectory(), "cookies.txt");
        }

        public static void ClearSavedCookies()
        {
            try
            {
                string path = GetSavedCookiesFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        public static string GetDefaultMusicDirectory()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string musicDir = Path.Combine(downloads, "SsshmulMusic");
            if (!Directory.Exists(musicDir))
            {
                try { Directory.CreateDirectory(musicDir); } catch { }
            }
            return musicDir;
        }

        public static string GetDefaultArtistTrackerDirectory()
        {
            string musicDir = GetDefaultMusicDirectory();
            string trackerDir = Path.Combine(musicDir, "מעקב אמנים");
            if (!Directory.Exists(trackerDir))
            {
                try { Directory.CreateDirectory(trackerDir); } catch { }
            }
            return trackerDir;
        }

        public static string GetArtistDirectory(string artistName)
        {
            string cleanName = SsshmulDownloader.ArtistTracker.DeduplicationEngine.CleanArtistName(artistName);
            string safeFolder = SsshmulDownloader.ArtistTracker.DeduplicationEngine.SanitizeFolderName(cleanName);
            if (string.IsNullOrWhiteSpace(safeFolder)) safeFolder = "אמן לא ידוע";

            string baseTrackerDir = !string.IsNullOrWhiteSpace(SettingsStore.Current.ArtistTrackerSavePath)
                ? SettingsStore.Current.ArtistTrackerSavePath
                : GetDefaultArtistTrackerDirectory();

            string artistDir = Path.Combine(baseTrackerDir, safeFolder);
            if (!Directory.Exists(artistDir))
            {
                try { Directory.CreateDirectory(artistDir); } catch { }
            }
            return artistDir;
        }

        public static string GetIconPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string ico = Path.Combine(baseDir, "icon.ico");
            if (File.Exists(ico)) return ico;
            string webIco = Path.Combine(baseDir, "web", "icon.ico");
            if (File.Exists(webIco)) return webIco;
            return ico;
        }
    }
}
