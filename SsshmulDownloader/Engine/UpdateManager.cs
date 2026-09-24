using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SsshmulDownloader.Engine
{
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? PublishedAt { get; set; }
    }

    public static class UpdateManager
    {
        private const string GitHubRepo = "SSSHMUEL/-Ssshmul-Downloader";
        private static readonly HttpClient _httpClient = new();

        static UpdateManager()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SsshmulDownloader-Updater");
        }

        public static string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                int plusIdx = infoVer.IndexOf('+');
                return plusIdx > 0 ? infoVer[..plusIdx] : infoVer;
            }
            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }

        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            string currentVer = GetCurrentVersion();
            var updateInfo = new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = currentVer,
                HasUpdate = false
            };

            try
            {
                string apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
                using var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return updateInfo;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.GetProperty("tag_name").GetString() ?? "";
                string cleanTag = tagName.TrimStart('v', 'V');
                string body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                string publishedAt = root.TryGetProperty("published_at", out var pubEl) ? pubEl.GetString() ?? "" : "";

                updateInfo.LatestVersion = cleanTag;
                updateInfo.ReleaseNotes = body;
                updateInfo.PublishedAt = publishedAt;

                // Find .exe asset
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            updateInfo.DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }

                if (IsNewerVersion(currentVer, cleanTag))
                {
                    updateInfo.HasUpdate = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateManager] Error checking updates: {ex.Message}");
            }

            return updateInfo;
        }

        public static async Task<string> DownloadAndLaunchInstallerAsync(string downloadUrl, Action<int>? onProgress = null)
        {
            string tempInstaller = Path.Combine(Path.GetTempPath(), $"SsshmulDownloader_Setup_{Guid.NewGuid():N}.exe");

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempInstaller, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        int percent = (int)((totalRead * 100) / totalBytes.Value);
                        onProgress?.Invoke(percent);
                    }
                }
            }

            // Launch the installer silently or with normal UI and close the current app
            var psi = new ProcessStartInfo
            {
                FileName = tempInstaller,
                Arguments = "/SILENT", // Or without /SILENT if user wants the installer wizard
                UseShellExecute = true
            };
            
            Process.Start(psi);
            
            // Exit current application cleanly
            Environment.Exit(0);
            return tempInstaller;
        }

        private static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            if (Version.TryParse(currentVersion, out var cur) && Version.TryParse(latestVersion, out var lat))
            {
                return lat > cur;
            }
            return string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
