using System;
using System.IO;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.Engine
{
    public static class BinaryResolver
    {
        public static string YtDlpPath => ResolveBinary("yt-dlp.exe");
        public static string FFmpegPath => ResolveBinary("ffmpeg.exe");
        public static string QjsPath => ResolveBinary("qjs.exe");

        public static bool BinariesAvailable => File.Exists(YtDlpPath) && File.Exists(FFmpegPath);

        private static string ResolveBinary(string binaryName)
        {
            string binDir = PathUtils.GetBinDirectory();
            string fullPath = Path.Combine(binDir, binaryName);
            if (File.Exists(fullPath)) return fullPath;

            // Check current directory / app base directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(appDir, binaryName);
            if (File.Exists(localPath)) return localPath;

            string localBin = Path.Combine(appDir, "bin_tools", binaryName);
            if (File.Exists(localBin)) return localBin;

            return fullPath;
        }
    }
}
