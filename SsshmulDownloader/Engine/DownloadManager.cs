using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SsshmulDownloader.ArtistTracker;
using SsshmulDownloader.Models;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.Engine
{
    public class DownloadManager
    {
        private static readonly Lazy<DownloadManager> _instance = new(() => new DownloadManager());
        public static DownloadManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, DownloadContext> _activeDownloads = new();
        public event Action<DownloadMessage>? MessageBroadcast;

        public void Broadcast(DownloadMessage message)
        {
            MessageBroadcast?.Invoke(message);
        }

        public List<ActiveDownloadState> GetActiveStates()
        {
            return _activeDownloads.Values
                .Where(ctx => !ctx.IsCancelled)
                .Select(ctx => ctx.ToActiveState())
                .ToList();
        }

        public void StartAdvancedDownload(
            string downloadId,
            string url,
            string? directUrl,
            string? customTitle,
            string? customThumbnail,
            string? formatId,
            string? destinationPath,
            bool isNetfree,
            bool isVideo,
            string? playlistTitle,
            string? qualityText,
            string? cookies,
            SubsOptions? subs,
            TagMappings? tagMappings)
        {
            if (_activeDownloads.TryGetValue(downloadId, out var existingCtx))
            {
                if (existingCtx.IsPaused)
                {
                    existingCtx.IsPaused = false;
                    existingCtx.IsCancelled = false;
                    existingCtx.Cts = new CancellationTokenSource();
                    existingCtx.CurrentState = "reconnecting";
                    _ = RunDownloadTask(existingCtx);
                    return;
                }
            }

            var ctx = new DownloadContext
            {
                DownloadId = downloadId,
                Url = url,
                DirectUrl = directUrl,
                CustomTitle = customTitle,
                CustomThumbnail = customThumbnail,
                FormatId = formatId,
                DestinationPath = !string.IsNullOrWhiteSpace(destinationPath) ? destinationPath : SettingsStore.Current.CustomSavePath,
                IsNetfree = isNetfree,
                IsVideo = isVideo,
                PlaylistTitle = playlistTitle,
                QualityText = qualityText,
                Cookies = cookies,
                Subs = subs,
                TagMappings = tagMappings,
                Title = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : "טוען נתונים...",
                Thumbnail = !string.IsNullOrWhiteSpace(customThumbnail) ? customThumbnail : "icon.png",
                CurrentState = "requesting"
            };

            _activeDownloads[downloadId] = ctx;
            _ = RunDownloadTask(ctx);
        }

        public void PauseAdvancedDownload(string downloadId)
        {
            if (_activeDownloads.TryGetValue(downloadId, out var ctx))
            {
                ctx.IsPaused = true;
                ctx.CurrentState = "paused";
                ctx.Cts.Cancel();
                ProcessTreeHelper.KillProcessTree(ctx.Process);
                Broadcast(new DownloadMessage("paused") { DownloadId = downloadId });
            }
        }

        public void ResumeAdvancedDownload(string downloadId)
        {
            if (_activeDownloads.TryGetValue(downloadId, out var ctx))
            {
                if (ctx.IsPaused)
                {
                    ctx.IsPaused = false;
                    ctx.IsCancelled = false;
                    ctx.Cts = new CancellationTokenSource();
                    ctx.CurrentState = "reconnecting";
                    Broadcast(new DownloadMessage("resumed") { DownloadId = downloadId });
                    _ = RunDownloadTask(ctx);
                }
            }
        }

        public void PauseAllAdvancedDownloads()
        {
            var keys = _activeDownloads.Keys.ToList();
            foreach (var key in keys)
            {
                PauseAdvancedDownload(key);
            }
        }

        public void ResumeAllAdvancedDownloads()
        {
            var keys = _activeDownloads.Keys.ToList();
            foreach (var key in keys)
            {
                ResumeAdvancedDownload(key);
            }
        }

        public void CancelAdvancedDownload(string downloadId)
        {
            if (_activeDownloads.TryRemove(downloadId, out var ctx))
            {
                ctx.IsCancelled = true;
                ctx.IsPaused = false;
                ctx.CurrentState = "cancelled";
                ctx.Cts.Cancel();
                ProcessTreeHelper.KillProcessTree(ctx.Process);

                if (!string.IsNullOrEmpty(ctx.TempDir) && Directory.Exists(ctx.TempDir))
                {
                    try { Directory.Delete(ctx.TempDir, recursive: true); } catch { }
                }

                Broadcast(DownloadMessage.Cancelled(downloadId));
            }
        }

        public void CancelAllAdvancedDownloads()
        {
            var keys = _activeDownloads.Keys.ToList();
            foreach (var key in keys)
            {
                CancelAdvancedDownload(key);
            }
        }

        public async Task StartQueueDownloadAsync(
            List<string> urls,
            string? formatId,
            string? destinationPath,
            bool isNetfree,
            string? playlistTitle,
            string? language)
        {
            int successCount = 0;
            int failureCount = 0;
            var successfulFiles = new List<string>();

            string finalFolder = !string.IsNullOrWhiteSpace(destinationPath)
                ? destinationPath
                : (SettingsStore.Current.CustomSavePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

            if (!string.IsNullOrWhiteSpace(playlistTitle))
            {
                string safeName = DeduplicationEngine.SanitizeFolderName(playlistTitle);
                string prefix = language == "he" ? "פלייליסט" : "Playlist";
                finalFolder = Path.Combine(finalFolder, $"{prefix} - {safeName}");
                Directory.CreateDirectory(finalFolder);
            }

            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                Broadcast(DownloadMessage.PlaylistProgress((i + 1).ToString(), urls.Count.ToString()));

                string itemId = "queue_" + Guid.NewGuid().ToString("N");
                var ctx = new DownloadContext
                {
                    DownloadId = itemId,
                    Url = url,
                    FormatId = formatId,
                    DestinationPath = finalFolder,
                    IsNetfree = isNetfree,
                    IsVideo = formatId != null && formatId.Contains("mp4")
                };

                var res = await YtDlpProcess.ExecuteDownloadAsync(ctx, Broadcast, CancellationToken.None);
                if (res.Success)
                {
                    successCount++;
                    if (!string.IsNullOrEmpty(res.FinalFilePath))
                    {
                        successfulFiles.Add(Path.GetFileName(res.FinalFilePath));
                    }
                }
                else
                {
                    failureCount++;
                }
            }

            Broadcast(DownloadMessage.QueueComplete(successCount, failureCount, successfulFiles, finalFolder));
        }

        private async Task RunDownloadTask(DownloadContext ctx)
        {
            try
            {
                var result = await YtDlpProcess.ExecuteDownloadAsync(ctx, Broadcast, ctx.Cts.Token);
                if (ctx.IsCancelled)
                {
                    Broadcast(DownloadMessage.Cancelled(ctx.DownloadId));
                }
                else if (result.Success)
                {
                    ctx.CurrentState = "completed";
                    Broadcast(DownloadMessage.Success(result.FinalFilePath ?? ctx.DestinationPath ?? "", ctx.DownloadId));
                    _activeDownloads.TryRemove(ctx.DownloadId, out _);
                }
                else if (ctx.IsPaused)
                {
                    // Stay in active downloads for resume
                }
                else
                {
                    ctx.CurrentState = "error";
                    Broadcast(DownloadMessage.CreateError(result.ErrorMessage ?? "שגיאה בהורדה", ctx.DownloadId));
                    _activeDownloads.TryRemove(ctx.DownloadId, out _);
                }
            }
            catch (Exception ex)
            {
                if (!ctx.IsCancelled && !ctx.IsPaused)
                {
                    Broadcast(DownloadMessage.CreateError(ex.Message, ctx.DownloadId));
                    _activeDownloads.TryRemove(ctx.DownloadId, out _);
                }
            }
        }
    }
}
