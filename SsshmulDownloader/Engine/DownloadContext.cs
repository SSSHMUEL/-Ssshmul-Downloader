using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SsshmulDownloader.Models;

namespace SsshmulDownloader.Engine
{
    public class DownloadContext
    {
        public string DownloadId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? DirectUrl { get; set; }
        public string? CustomTitle { get; set; }
        public string? CustomThumbnail { get; set; }
        public string? FormatId { get; set; }
        public string? DestinationPath { get; set; }
        public bool IsNetfree { get; set; }
        public bool IsVideo { get; set; }
        public string? PlaylistTitle { get; set; }
        public string? QualityText { get; set; }
        public string? Cookies { get; set; }
        public SubsOptions? Subs { get; set; }
        public TagMappings? TagMappings { get; set; }

        public Process? Process { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
        public bool IsPaused { get; set; }
        public bool IsCancelled { get; set; }

        public string CurrentState { get; set; } = "requesting";
        public string LastPercent { get; set; } = "0";
        public string LastSpeed { get; set; } = "0 MB/s";
        public string Title { get; set; } = "טוען נתונים...";
        public string Thumbnail { get; set; } = "icon.png";

        public string? TempDir { get; set; }
        public long CreationTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public ActiveDownloadState ToActiveState()
        {
            return new ActiveDownloadState
            {
                Id = DownloadId,
                Url = Url,
                IsVideo = IsVideo,
                QualityText = QualityText ?? (IsVideo ? "MP4 וידאו" : "MP3 שמע"),
                Title = Title,
                Thumbnail = Thumbnail,
                IsPaused = IsPaused,
                FormatId = FormatId,
                DestinationPath = DestinationPath,
                IsNetfree = IsNetfree,
                PlaylistTitle = PlaylistTitle,
                CurrentState = CurrentState,
                LastPercent = LastPercent,
                LastSpeed = LastSpeed
            };
        }
    }
}
