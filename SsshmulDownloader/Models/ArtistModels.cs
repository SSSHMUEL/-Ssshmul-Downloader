using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SsshmulDownloader.Models
{
    public class ArtistSong
    {
        [JsonPropertyName("videoId")]
        public string VideoId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("uploadDate")]
        public string UploadDate { get; set; } = string.Empty;

        [JsonPropertyName("duration")]
        public string Duration { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; } = string.Empty;

        [JsonPropertyName("isDownloaded")]
        public bool IsDownloaded { get; set; }

        [JsonPropertyName("downloadedAt")]
        public long DownloadedAt { get; set; }

        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("isOfficialAudio")]
        public bool IsOfficialAudio { get; set; }

        public ArtistSong() { }

        public ArtistSong(string videoId, string title, string url, string uploadDate, string duration, string thumbnail, bool isOfficialAudio = false)
        {
            VideoId = videoId;
            Title = title;
            Url = url;
            UploadDate = uploadDate;
            Duration = duration;
            Thumbnail = thumbnail;
            IsDownloaded = false;
            IsOfficialAudio = isOfficialAudio;
        }
    }

    public class TrackedArtist
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("channelUrl")]
        public string ChannelUrl { get; set; } = string.Empty;

        [JsonPropertyName("avatarUrl")]
        public string AvatarUrl { get; set; } = string.Empty;

        [JsonPropertyName("downloadFolder")]
        public string? DownloadFolder { get; set; }

        [JsonPropertyName("preferredFormat")]
        public string PreferredFormat { get; set; } = "mp3_high";

        [JsonPropertyName("autoDownload")]
        public bool AutoDownload { get; set; } = true;

        [JsonPropertyName("filterTopicOnly")]
        public bool FilterTopicOnly { get; set; } = false;

        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        [JsonPropertyName("lastScannedAt")]
        public long LastScannedAt { get; set; }

        [JsonPropertyName("newSongsCount")]
        public int NewSongsCount { get; set; }

        [JsonPropertyName("knownVideoIds")]
        public HashSet<string> KnownVideoIds { get; set; } = new HashSet<string>();

        [JsonPropertyName("recentSongs")]
        public List<ArtistSong> RecentSongs { get; set; } = new List<ArtistSong>();

        public TrackedArtist() { }

        public TrackedArtist(string id, string name, string channelUrl, string avatarUrl)
        {
            Id = id;
            Name = name;
            ChannelUrl = channelUrl;
            AvatarUrl = avatarUrl;
        }
    }
}
