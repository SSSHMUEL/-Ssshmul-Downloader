using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SsshmulDownloader.Engine;
using SsshmulDownloader.Models;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.ArtistTracker
{
    public static class ArtistScanner
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly SemaphoreSlim _scanLock = new(1, 1);
        public static bool IsScanningAll { get; private set; }

        public static event Action<string, string>? NotificationTriggered;

        public static async Task<TrackedArtist?> ResolveArtistInfoAsync(string inputQuery, string? cookies = null)
        {
            if (string.IsNullOrWhiteSpace(inputQuery)) return null;

            string normalized = inputQuery.Trim();
            if (normalized.StartsWith("https://music.youtube.com/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://www.youtube.com/" + normalized.Substring("https://music.youtube.com/".Length);
            }
            else if (normalized.StartsWith("http://music.youtube.com/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://www.youtube.com/" + normalized.Substring("http://music.youtube.com/".Length);
            }

            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.StartsWith("@"))
                {
                    normalized = "https://www.youtube.com/" + normalized;
                }
                else
                {
                    normalized = "ytsearch1:" + normalized;
                }
            }

            var res = await RunResolveAttemptAsync(inputQuery, normalized, cookies);
            if (res == null && (!string.IsNullOrEmpty(cookies) || File.Exists(PathUtils.GetSavedCookiesFilePath())))
            {
                PathUtils.ClearSavedCookies();
                res = await RunResolveAttemptAsync(inputQuery, normalized, null);
            }
            return res;
        }

        private static async Task<TrackedArtist?> RunResolveAttemptAsync(string inputQuery, string normalized, string? cookies)
        {
            string ytdlpPath = BinaryResolver.YtDlpPath;
            if (!File.Exists(ytdlpPath)) return null;

            string logFile = PathUtils.GetLogFilePath();

            var psi = new ProcessStartInfo
            {
                FileName = ytdlpPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("--encoding");
            psi.ArgumentList.Add("utf-8");
            psi.ArgumentList.Add("--flat-playlist");
            psi.ArgumentList.Add("--dump-single-json");
            psi.ArgumentList.Add("--playlist-items");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("--no-check-certificates");
            psi.ArgumentList.Add("--prefer-insecure");
            psi.ArgumentList.Add("--force-ipv4");
            psi.ArgumentList.Add("--socket-timeout");
            psi.ArgumentList.Add("15");
            psi.ArgumentList.Add("--extractor-args");
            psi.ArgumentList.Add("youtube:player_client=ios,android,web_creator");
            psi.ArgumentList.Add("--user-agent");
            psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

            string qjsPath = BinaryResolver.QjsPath;
            if (File.Exists(qjsPath))
            {
                psi.ArgumentList.Add("--js-runtimes");
                psi.ArgumentList.Add($"quickjs:{qjsPath}");
            }

            string? tempCookies = null;
            bool hasCookies = false;
            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    string cleanedCookies = cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleanedCookies.EndsWith("\n")) cleanedCookies += "\n";
                    tempCookies = Path.Combine(Path.GetTempPath(), $"sshmul_artist_resolve_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(tempCookies, cleanedCookies, new UTF8Encoding(false));
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(tempCookies);
                    hasCookies = true;
                }
                catch { }
            }
            else
            {
                string savedCookies = PathUtils.GetSavedCookiesFilePath();
                if (File.Exists(savedCookies))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(savedCookies);
                    hasCookies = true;
                }
            }

            psi.ArgumentList.Add(normalized);

            string binDir = Path.GetDirectoryName(ytdlpPath) ?? "";
            string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = binDir + ";" + existingPath;

            try
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Resolving artist: '{inputQuery}' (Target: '{normalized}') (Cookies: {hasCookies})\n");

                using var process = new Process { StartInfo = psi };
                process.Start();

                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string output = await outTask;
                string error = await errTask;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Resolve yt-dlp stderr: {error.Trim()}\n");
                    if (error.Contains("cookies are no longer valid", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("The provided YouTube account cookies", StringComparison.OrdinalIgnoreCase))
                    {
                        PathUtils.ClearSavedCookies();
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Invalid/expired cookies detected and cleared automatically.\n");
                    }
                }

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(output);
                        var root = doc.RootElement;
                        var node = root;

                        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0)
                        {
                            node = entries[0];
                        }

                        // Try to get channel information first from root (if root is playlist/channel) or node
                        string? channelId = GetStringProp(root, "channel_id") ?? GetStringProp(root, "id") ?? GetStringProp(node, "channel_id") ?? GetStringProp(node, "uploader_id");
                        string? channelTitle = GetStringProp(root, "channel") ?? GetStringProp(root, "uploader");
                        if (string.IsNullOrWhiteSpace(channelTitle))
                        {
                            string? rootTitle = GetStringProp(root, "title");
                            if (!string.IsNullOrWhiteSpace(rootTitle) && !rootTitle.EndsWith(" - Videos", StringComparison.OrdinalIgnoreCase) && !rootTitle.EndsWith(" - Releases", StringComparison.OrdinalIgnoreCase))
                            {
                                channelTitle = rootTitle;
                            }
                            else
                            {
                                channelTitle = GetStringProp(node, "channel") ?? GetStringProp(node, "uploader") ?? GetStringProp(node, "title") ?? inputQuery;
                            }
                        }
                        channelTitle = DeduplicationEngine.CleanArtistName(channelTitle);

                        string? channelUrl = GetStringProp(root, "channel_url") ?? GetStringProp(root, "uploader_url") ?? GetStringProp(node, "channel_url") ?? GetStringProp(node, "uploader_url");
                        if (string.IsNullOrEmpty(channelUrl) || channelUrl.StartsWith("ytsearch"))
                        {
                            if (!string.IsNullOrEmpty(channelId) && channelId.StartsWith("UC"))
                            {
                                channelUrl = "https://www.youtube.com/channel/" + channelId;
                            }
                            else if (normalized.StartsWith("http"))
                            {
                                channelUrl = normalized;
                            }
                            else
                            {
                                channelUrl = "ytsearch30:" + channelTitle;
                            }
                        }

                        string avatarUrl = "";
                        var thumbElem = root.TryGetProperty("thumbnails", out var rThumbs) && rThumbs.ValueKind == JsonValueKind.Array && rThumbs.GetArrayLength() > 0 ? rThumbs : (node.TryGetProperty("thumbnails", out var nThumbs) && nThumbs.ValueKind == JsonValueKind.Array && nThumbs.GetArrayLength() > 0 ? nThumbs : default);
                        if (thumbElem.ValueKind == JsonValueKind.Array && thumbElem.GetArrayLength() > 0)
                        {
                            avatarUrl = thumbElem[thumbElem.GetArrayLength() - 1].GetProperty("url").GetString() ?? "";
                        }

                        if (string.IsNullOrEmpty(channelId))
                        {
                            channelId = "artist_" + Math.Abs(channelTitle.GetHashCode());
                        }

                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Successfully resolved artist: '{channelTitle}' (Channel: {channelUrl})\n");
                        return new TrackedArtist(channelId, channelTitle, channelUrl, avatarUrl);
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] JSON parse error: {ex.Message}\n");
                        Debug.WriteLine($"Error parsing artist JSON: {ex.Message}");
                    }
                }
                else
                {
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Resolve failed with exit code: {process.ExitCode}\n");
                    if (hasCookies)
                    {
                        PathUtils.ClearSavedCookies();
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Resolve exception: {ex.Message}\n");
            }
            finally
            {
                if (tempCookies != null && File.Exists(tempCookies))
                {
                    try { File.Delete(tempCookies); } catch { }
                }
            }

            return null;
        }

        public static async Task<List<ArtistSong>> ScanArtistAsync(TrackedArtist artist, bool triggerDownload, string? cookies = null)
        {
            var newSongs = new List<ArtistSong>();
            string ytdlpPath = BinaryResolver.YtDlpPath;
            if (!File.Exists(ytdlpPath)) return newSongs;

            string logFile = PathUtils.GetLogFilePath();
            string targetUrl = artist.ChannelUrl;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                targetUrl = "ytsearch30:" + artist.Name;
            }

            string cleanName = DeduplicationEngine.CleanArtistName(artist.Name);
            var fetchedSongs = new List<ArtistSong>();

            // STEP 1: Fast official discography lookup (iTunes / Apple Music Music Catalog)
            // Retrieves the 100% clean, official track list directly without any vlogs, live concerts, or videos
            var officialDiscography = await FetchOfficialDiscographyTracksAsync(artist.Name, logFile);
            if (officialDiscography.Count > 0)
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Found {officialDiscography.Count} official studio tracks in catalog for '{artist.Name}'. Resolving direct YouTube audio tracks...\n");
                fetchedSongs = await ResolveDiscographyToYouTubeTracksAsync(artist, officialDiscography, cookies, ytdlpPath, logFile);
            }

            // STEP 2: Fallback to official releases / Topic channel scan if discography was not found in catalog
            if (fetchedSongs.Count == 0)
            {
                var scanUrls = new List<string>();
                if (targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string baseChan = targetUrl;
                    if (baseChan.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                        baseChan = baseChan.Substring(0, baseChan.Length - "/videos".Length);
                    else if (baseChan.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
                        baseChan = baseChan.Substring(0, baseChan.Length - "/releases".Length);

                    // Priority 1: Releases tab (Official album / single tracks)
                    scanUrls.Add(baseChan + "/releases");
                }

                // Priority 2: Precise Topic music search
                scanUrls.Add($"ytsearch50:{cleanName} topic music");

                fetchedSongs = await FetchArtistSongsFromUrlsAsync(artist, scanUrls, cookies, ytdlpPath, logFile);
                if (fetchedSongs.Count == 0 && (!string.IsNullOrEmpty(cookies) || File.Exists(PathUtils.GetSavedCookiesFilePath())))
                {
                    PathUtils.ClearSavedCookies();
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Retrying artist scan without cookies for '{artist.Name}'...\n");
                    fetchedSongs = await FetchArtistSongsFromUrlsAsync(artist, scanUrls, null, ytdlpPath, logFile);
                }
            }

            // Check if artist was deleted during fetching
            if (ArtistStore.GetById(artist.Id) == null)
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Scan aborted for '{artist.Name}' because artist was deleted.\n");
                return newSongs;
            }

            // CRITICAL REQUIREMENT: Intelligent Deduplication!
            // Only selects the best official audio/song version and completely avoids duplicate music video tracks
            var deduplicatedSongs = DeduplicationEngine.DeduplicateArtistSongs(fetchedSongs, artist.Name);

            // Check which songs are already downloaded on disk in artist folder
            string destFolder = !string.IsNullOrWhiteSpace(artist.DownloadFolder)
                ? artist.DownloadFolder
                : PathUtils.GetArtistDirectory(cleanName);

            var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(destFolder))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(destFolder))
                    {
                        string fn = Path.GetFileNameWithoutExtension(f);
                        existingFiles.Add(fn);
                        string normFn = DeduplicationEngine.NormalizeSongTitle(fn, cleanName);
                        if (!string.IsNullOrWhiteSpace(normFn)) existingFiles.Add(normFn);
                    }
                }
                catch { }
            }

            var known = artist.KnownVideoIds;
            foreach (var song in deduplicatedSongs)
            {
                // Check if file already exists on disk
                string normSong = DeduplicationEngine.NormalizeSongTitle(song.Title, cleanName);
                if (existingFiles.Contains(song.Title) || (!string.IsNullOrWhiteSpace(normSong) && existingFiles.Contains(normSong)))
                {
                    song.IsDownloaded = true;
                }

                if (!known.Contains(song.VideoId))
                {
                    newSongs.Add(song);
                }
            }

            // Check again if artist was deleted
            if (ArtistStore.GetById(artist.Id) == null)
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Scan save/download aborted for '{artist.Name}' because artist was deleted.\n");
                return newSongs;
            }

            // Update artist model
            foreach (var s in deduplicatedSongs)
            {
                artist.KnownVideoIds.Add(s.VideoId);
            }

            if (deduplicatedSongs.Count > 0)
            {
                artist.RecentSongs = deduplicatedSongs.Take(250).ToList();
            }

            artist.LastScannedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Auto-download new songs if enabled
            if (triggerDownload && artist.AutoDownload && newSongs.Count > 0)
            {
                var unDownloaded = newSongs.Where(s => !s.IsDownloaded).ToList();
                if (unDownloaded.Count > 0)
                {
                    DownloadNewArtistSongs(artist, unDownloaded);
                }
            }

            int unDownloadedCount = artist.RecentSongs.Count(s => !s.IsDownloaded);
            artist.NewSongsCount = unDownloadedCount;
            ArtistStore.Save(artist);

            // Broadcast artist update
            var updateMsg = new DownloadMessage("artist_updated")
            {
                Artist = artist,
                NewSongsCount = unDownloadedCount
            };
            DownloadManager.Instance.Broadcast(updateMsg);

            return newSongs;
        }

        public static async Task ScanAllArtistsAsync(bool triggerDownload)
        {
            if (!await _scanLock.WaitAsync(100))
            {
                return; // Scan already in progress
            }

            IsScanningAll = true;
            try
            {
                DownloadManager.Instance.Broadcast(new DownloadMessage("scan_all_started")
                {
                    Message = "מתחיל בסריקת כל האמנים במעקב..."
                });

                var allArtists = ArtistStore.GetAll();
                int totalNewSongs = 0;

                for (int i = 0; i < allArtists.Count; i++)
                {
                    var artist = allArtists[i];
                    DownloadManager.Instance.Broadcast(new DownloadMessage("scan_progress")
                    {
                        Message = $"סורק את {artist.Name} ({i + 1}/{allArtists.Count})"
                    });

                    var newSongs = await ScanArtistAsync(artist, triggerDownload);
                    totalNewSongs += newSongs.Count;
                }

                DownloadManager.Instance.Broadcast(new DownloadMessage("scan_all_completed")
                {
                    TotalNewSongs = totalNewSongs,
                    Message = $"סריקת האמנים הושלמה. נמצאו {totalNewSongs} שירים חדשים."
                });

                if (totalNewSongs > 0)
                {
                    NotificationTriggered?.Invoke("Ssshmul Downloader - שירים חדשים!",
                        $"נמצאו והורדו {totalNewSongs} שירים חדשים מהאמנים במעקב שלך 🎵");
                }
            }
            finally
            {
                IsScanningAll = false;
                _scanLock.Release();
            }
        }

        private static void DownloadNewArtistSongs(TrackedArtist artist, List<ArtistSong> songs)
        {
            string cleanName = DeduplicationEngine.CleanArtistName(artist.Name);
            string destFolder = !string.IsNullOrWhiteSpace(artist.DownloadFolder)
                ? artist.DownloadFolder
                : PathUtils.GetArtistDirectory(cleanName);

            try
            {
                if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);
            }
            catch { }

            bool isVideo = artist.PreferredFormat?.Contains("mp4") == true || 
                           artist.PreferredFormat?.Contains("bestvideo") == true || 
                           artist.PreferredFormat?.Contains("video") == true;

            string format = !string.IsNullOrWhiteSpace(artist.PreferredFormat)
                ? artist.PreferredFormat 
                : (isVideo ? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best" : "mp3_high");

            foreach (var song in songs)
            {
                string downloadId = $"artist_{song.VideoId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                DownloadManager.Instance.StartAdvancedDownload(
                    downloadId: downloadId,
                    url: song.Url,
                    directUrl: null,
                    customTitle: song.Title,
                    customThumbnail: song.Thumbnail,
                    formatId: format,
                    destinationPath: destFolder,
                    isNetfree: true,
                    isVideo: isVideo,
                    playlistTitle: null,
                    qualityText: isVideo ? "הורדת וידאו (מעקב אמן)" : "הורדת שמע (מעקב אמן)",
                    cookies: null,
                    subs: null,
                    tagMappings: null
                );

                song.IsDownloaded = true;
                song.DownloadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        private static async Task<List<ArtistSong>> FetchArtistSongsFromUrlsAsync(
            TrackedArtist artist,
            List<string> scanUrls,
            string? cookies,
            string ytdlpPath,
            string logFile)
        {
            var fetchedSongs = new List<ArtistSong>();
            string? tempCookies = null;
            bool hasCookies = false;

            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    string cleanedCookies = cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleanedCookies.EndsWith("\n")) cleanedCookies += "\n";
                    tempCookies = Path.Combine(Path.GetTempPath(), $"sshmul_artist_scan_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(tempCookies, cleanedCookies, new UTF8Encoding(false));
                    hasCookies = true;
                }
                catch { }
            }
            else
            {
                string savedCookies = PathUtils.GetSavedCookiesFilePath();
                if (File.Exists(savedCookies))
                {
                    hasCookies = true;
                }
            }

            try
            {
                foreach (var tryUrl in scanUrls)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ytdlpPath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    psi.ArgumentList.Add("--encoding");
                    psi.ArgumentList.Add("utf-8");
                    psi.ArgumentList.Add("--flat-playlist");
                    psi.ArgumentList.Add("--dump-json");
                    psi.ArgumentList.Add("--no-check-certificates");
                    psi.ArgumentList.Add("--prefer-insecure");
                    psi.ArgumentList.Add("--force-ipv4");
                    psi.ArgumentList.Add("--socket-timeout");
                    psi.ArgumentList.Add("15");
                    psi.ArgumentList.Add("--extractor-args");
                    psi.ArgumentList.Add("youtube:player_client=ios,android,web_creator");
                    psi.ArgumentList.Add("--user-agent");
                    psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

                    string qjsPath = BinaryResolver.QjsPath;
                    if (File.Exists(qjsPath))
                    {
                        psi.ArgumentList.Add("--js-runtimes");
                        psi.ArgumentList.Add($"quickjs:{qjsPath}");
                    }

                    if (tempCookies != null && File.Exists(tempCookies))
                    {
                        psi.ArgumentList.Add("--cookies");
                        psi.ArgumentList.Add(tempCookies);
                    }
                    else if (hasCookies)
                    {
                        string savedCookies = PathUtils.GetSavedCookiesFilePath();
                        if (File.Exists(savedCookies))
                        {
                            psi.ArgumentList.Add("--cookies");
                            psi.ArgumentList.Add(savedCookies);
                        }
                    }

                    psi.ArgumentList.Add(tryUrl);

                    string binDir = Path.GetDirectoryName(ytdlpPath) ?? "";
                    string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    psi.Environment["PATH"] = binDir + ";" + existingPath;

                    using var process = new Process { StartInfo = psi };
                    try
                    {
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Scanning URL for '{artist.Name}': {tryUrl} (Cookies: {hasCookies})\n");

                        process.Start();

                        var errBuilder = new StringBuilder();
                        var errTask = Task.Run(async () =>
                        {
                            while (!process.StandardError.EndOfStream)
                            {
                                string? errLine = await process.StandardError.ReadLineAsync();
                                if (errLine != null) errBuilder.AppendLine(errLine);
                            }
                        });

                        while (!process.StandardOutput.EndOfStream)
                        {
                            string? line = await process.StandardOutput.ReadLineAsync();
                            if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{")) continue;

                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;

                                string? videoId = GetStringProp(root, "id");
                                string title = GetStringProp(root, "title") ?? "";
                                string url = GetStringProp(root, "url") ?? (videoId != null ? $"https://www.youtube.com/watch?v={videoId}" : "");
                                string uploadDate = GetStringProp(root, "upload_date") ?? "";
                                string duration = GetStringProp(root, "duration") ?? "";
                                string thumb = "";

                                if (root.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0)
                                {
                                    thumb = thumbs[thumbs.GetArrayLength() - 1].GetProperty("url").GetString() ?? "";
                                }
                                else if (!string.IsNullOrEmpty(videoId))
                                {
                                    thumb = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
                                }

                                if (!string.IsNullOrEmpty(videoId) && !DeduplicationEngine.IsShortOrPromo(title) && !url.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
                                {
                                    fetchedSongs.Add(new ArtistSong(videoId, title, url, uploadDate, duration, thumb));
                                }
                            }
                            catch { }
                        }

                        await Task.WhenAll(process.WaitForExitAsync(), errTask);

                        string stderr = errBuilder.ToString().Trim();
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Scan stderr: {stderr}\n");
                            if (stderr.Contains("cookies are no longer valid", StringComparison.OrdinalIgnoreCase) ||
                                stderr.Contains("The provided YouTube account cookies", StringComparison.OrdinalIgnoreCase))
                            {
                                PathUtils.ClearSavedCookies();
                            }
                        }

                        if (fetchedSongs.Count > 0)
                        {
                            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Running total: {fetchedSongs.Count} tracks found so far (from {tryUrl})\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Error scanning URL {tryUrl}: {ex.Message}\n");
                        Debug.WriteLine($"Error scanning artist URL {tryUrl}: {ex.Message}");
                    }
                }
            }
            finally
            {
                if (tempCookies != null && File.Exists(tempCookies))
                {
                    try { File.Delete(tempCookies); } catch { }
                }
            }

            return fetchedSongs;
        }

        private static async Task<List<string>> FetchOfficialDiscographyTracksAsync(string artistName, string logFile)
        {
            var tracks = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string cleanName = DeduplicationEngine.CleanArtistName(artistName);
                string lookupUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(cleanName)}&entity=musicArtist&limit=5";
                
                using var req = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");
                
                using var res = await _httpClient.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                    {
                        long? artistId = null;
                        foreach (var item in results.EnumerateArray())
                        {
                            if (item.TryGetProperty("artistId", out var idProp))
                            {
                                artistId = idProp.GetInt64();
                                break;
                            }
                        }

                        if (artistId.HasValue)
                        {
                            string songLookup = $"https://itunes.apple.com/lookup?id={artistId.Value}&entity=song&limit=200";
                            using var sReq = new HttpRequestMessage(HttpMethod.Get, songLookup);
                            sReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");
                            using var sRes = await _httpClient.SendAsync(sReq);
                            if (sRes.IsSuccessStatusCode)
                            {
                                string sJson = await sRes.Content.ReadAsStringAsync();
                                using var sDoc = JsonDocument.Parse(sJson);
                                if (sDoc.RootElement.TryGetProperty("results", out var sResults) && sResults.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var sItem in sResults.EnumerateArray())
                                    {
                                        string? wrapper = GetStringProp(sItem, "wrapperType");
                                        if (wrapper == "track")
                                        {
                                            string? trackName = GetStringProp(sItem, "trackName");
                                            if (!string.IsNullOrWhiteSpace(trackName))
                                            {
                                                if (DeduplicationEngine.IsVariantOrNonStudioTrack(trackName)) continue;
                                                string norm = DeduplicationEngine.NormalizeSongTitle(trackName, cleanName);
                                                if (!string.IsNullOrWhiteSpace(norm) && seen.Add(norm))
                                                {
                                                    tracks.Add(trackName.Trim());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Catalog discography lookup error: {ex.Message}\n");
            }

            return tracks;
        }

        private static async Task<List<ArtistSong>> ResolveDiscographyToYouTubeTracksAsync(
            TrackedArtist artist,
            List<string> trackTitles,
            string? cookies,
            string ytdlpPath,
            string logFile)
        {
            var resolvedSongs = new List<ArtistSong>();
            var seenVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string cleanArtist = DeduplicationEngine.CleanArtistName(artist.Name);

            // Step A: Filter trackTitles to remove remix / slowed / sped up / acoustic duplicates
            var filteredTitles = new List<string>();
            foreach (var t in trackTitles)
            {
                if (DeduplicationEngine.IsShortOrPromo(t) || DeduplicationEngine.IsVariantOrNonStudioTrack(t)) continue;
                string norm = DeduplicationEngine.NormalizeSongTitle(t, cleanArtist);
                if (!string.IsNullOrWhiteSpace(norm) && seenTitles.Add(norm))
                {
                    filteredTitles.Add(t);
                }
            }

            // Step B: Perform a fast bulk scan of the artist's Releases & Topic music
            var bulkScanUrls = new List<string>();

            if (!string.IsNullOrEmpty(artist.ChannelUrl) && artist.ChannelUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string baseChan = artist.ChannelUrl;
                if (baseChan.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                    baseChan = baseChan.Substring(0, baseChan.Length - "/videos".Length);
                else if (baseChan.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
                    baseChan = baseChan.Substring(0, baseChan.Length - "/releases".Length);

                bulkScanUrls.Add(baseChan + "/releases");
            }

            bulkScanUrls.Add($"ytsearch50:{cleanArtist} topic music");

            var allCandidatePool = await FetchArtistSongsFromUrlsAsync(artist, bulkScanUrls, cookies, ytdlpPath, logFile);

            // Step C: Match catalog songs against the candidate pool in memory (Instantaneous!)
            var poolByNormTitle = new Dictionary<string, List<ArtistSong>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allCandidatePool)
            {
                if (DeduplicationEngine.IsShortOrPromo(c.Title) || DeduplicationEngine.IsVariantOrNonStudioTrack(c.Title)) continue;
                string norm = DeduplicationEngine.NormalizeSongTitle(c.Title, cleanArtist);
                if (!string.IsNullOrWhiteSpace(norm))
                {
                    if (!poolByNormTitle.TryGetValue(norm, out var list))
                    {
                        list = new List<ArtistSong>();
                        poolByNormTitle[norm] = list;
                    }
                    list.Add(c);
                }
            }

            var missingTitles = new List<string>();
            foreach (var track in filteredTitles)
            {
                string norm = DeduplicationEngine.NormalizeSongTitle(track, cleanArtist);
                if (poolByNormTitle.TryGetValue(norm, out var matches) && matches.Count > 0)
                {
                    var best = matches.OrderByDescending(m => DeduplicationEngine.GetSongScore(m)).First();
                    if (!seenVideoIds.Contains(best.VideoId))
                    {
                        seenVideoIds.Add(best.VideoId);
                        resolvedSongs.Add(best);
                    }
                }
                else
                {
                    missingTitles.Add(track);
                }
            }

            // Step D: If any specific tracks were missing from bulk scan, search ONLY the artist's Topic channel concurrently
            if (missingTitles.Count > 0 && missingTitles.Count <= 35)
            {
                var semaphore = new SemaphoreSlim(3, 3); // 3 parallel queries with staggered timing to prevent 429 rate limits
                var lockObj = new object();

                var tasks = missingTitles.Select(async (track, index) =>
                {
                    await Task.Delay(index * 150); // slight stagger
                    await semaphore.WaitAsync();
                    try
                    {
                        if (ArtistStore.GetById(artist.Id) == null) return;
                        string searchTarget = $"ytsearch3:{cleanArtist} {track} audio";
                        var directMatches = await FetchArtistSongsFromUrlsAsync(artist, new List<string> { searchTarget }, cookies, ytdlpPath, logFile);
                        var cleanMatches = directMatches.Where(m => !DeduplicationEngine.IsShortOrPromo(m.Title) && !DeduplicationEngine.IsVariantOrNonStudioTrack(m.Title)).ToList();
                        if (cleanMatches.Count > 0)
                        {
                            var best = cleanMatches.OrderByDescending(m => DeduplicationEngine.GetSongScore(m)).First();
                            lock (lockObj)
                            {
                                string normBest = DeduplicationEngine.NormalizeSongTitle(best.Title, cleanArtist);
                                if (!seenVideoIds.Contains(best.VideoId) && (string.IsNullOrWhiteSpace(normBest) || seenTitles.Add(normBest)))
                                {
                                    seenVideoIds.Add(best.VideoId);
                                    resolvedSongs.Add(best);
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }

            var finalCleanSongs = DeduplicationEngine.DeduplicateArtistSongs(resolvedSongs, artist.Name);
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Fast discography resolution finished. Matched {finalCleanSongs.Count} clean studio tracks for '{artist.Name}'.\n");
            return finalCleanSongs;
        }

        private static string? GetStringProp(JsonElement elem, string prop)
        {
            if (elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }
            return null;
        }
    }
}
