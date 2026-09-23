using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private static readonly SemaphoreSlim _scanLock = new(1, 1);
        public static bool IsScanningAll { get; private set; }

        public static event Action<string, string>? NotificationTriggered;

        public static async Task<TrackedArtist?> ResolveArtistInfoAsync(string inputQuery, string? cookies = null)
        {
            string normalized = inputQuery.Trim();
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
            psi.ArgumentList.Add("--socket-timeout");
            psi.ArgumentList.Add("15");
            psi.ArgumentList.Add("--extractor-args");
            psi.ArgumentList.Add("youtube:player_client=android,ios,web");

            string? tempCookies = null;
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
                }
            }

            psi.ArgumentList.Add(normalized);

            string binDir = Path.GetDirectoryName(ytdlpPath) ?? "";
            string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = binDir + ";" + existingPath;

            try
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Resolving artist: '{inputQuery}' (Target: '{normalized}')\n");

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

                        string? channelId = GetStringProp(node, "channel_id") ?? GetStringProp(node, "uploader_id");
                        string channelTitle = GetStringProp(node, "channel") ?? GetStringProp(node, "uploader") ?? GetStringProp(node, "title") ?? inputQuery;
                        channelTitle = DeduplicationEngine.CleanArtistName(channelTitle);

                        string? channelUrl = GetStringProp(node, "channel_url") ?? GetStringProp(node, "uploader_url");
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
                        if (node.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0)
                        {
                            avatarUrl = thumbs[thumbs.GetArrayLength() - 1].GetProperty("url").GetString() ?? "";
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

            var scanUrls = new List<string>();
            if (targetUrl.StartsWith("http://") || targetUrl.StartsWith("https://"))
            {
                if (targetUrl.EndsWith("/videos"))
                {
                    scanUrls.Add(targetUrl);
                    scanUrls.Add(targetUrl.Substring(0, targetUrl.Length - "/videos".Length));
                }
                else
                {
                    scanUrls.Add(targetUrl + "/videos");
                    scanUrls.Add(targetUrl);
                }
            }
            else
            {
                scanUrls.Add(targetUrl);
            }

            string? tempCookies = null;
            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    string cleanedCookies = cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleanedCookies.EndsWith("\n")) cleanedCookies += "\n";
                    tempCookies = Path.Combine(Path.GetTempPath(), $"sshmul_artist_scan_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(tempCookies, cleanedCookies, new UTF8Encoding(false));
                }
                catch { }
            }

            var fetchedSongs = new List<ArtistSong>();

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
                psi.ArgumentList.Add("--socket-timeout");
                psi.ArgumentList.Add("15");
                psi.ArgumentList.Add("--extractor-args");
                psi.ArgumentList.Add("youtube:player_client=android,ios,web");

                if (tempCookies != null && File.Exists(tempCookies))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(tempCookies);
                }
                else
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
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Scanning URL for '{artist.Name}': {tryUrl}\n");

                    process.Start();

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

                            if (!string.IsNullOrEmpty(videoId) && !DeduplicationEngine.IsShortOrPromo(title))
                            {
                                fetchedSongs.Add(new ArtistSong(videoId, title, url, uploadDate, duration, thumb));
                            }
                        }
                        catch { }
                    }

                    await process.WaitForExitAsync();

                    if (fetchedSongs.Count > 0)
                    {
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Found {fetchedSongs.Count} tracks from {tryUrl}\n");
                        break; // Retrieved songs successfully from this URL
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Error scanning URL {tryUrl}: {ex.Message}\n");
                    Debug.WriteLine($"Error scanning artist URL {tryUrl}: {ex.Message}");
                }
            }

            if (tempCookies != null && File.Exists(tempCookies))
            {
                try { File.Delete(tempCookies); } catch { }
            }

            // CRITICAL REQUIREMENT: Intelligent Deduplication!
            // Only selects the best official audio/song version and completely avoids duplicate music video tracks
            var deduplicatedSongs = DeduplicationEngine.DeduplicateArtistSongs(fetchedSongs, artist.Name);

            var known = artist.KnownVideoIds;
            foreach (var song in deduplicatedSongs)
            {
                if (!known.Contains(song.VideoId))
                {
                    newSongs.Add(song);
                }
            }

            // Update artist model
            foreach (var s in deduplicatedSongs)
            {
                artist.KnownVideoIds.Add(s.VideoId);
            }

            if (deduplicatedSongs.Count > 0)
            {
                artist.RecentSongs = deduplicatedSongs.Take(50).ToList();
            }

            artist.LastScannedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            artist.NewSongsCount = newSongs.Count;
            ArtistStore.Save(artist);

            // Auto-download new songs if enabled
            if (triggerDownload && artist.AutoDownload && newSongs.Count > 0)
            {
                DownloadNewArtistSongs(artist, newSongs);
            }

            // Broadcast artist update
            var updateMsg = new DownloadMessage("artist_updated")
            {
                Artist = artist,
                NewSongsCount = newSongs.Count
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
