using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SsshmulDownloader.Models
{
    public class ActiveDownloadState
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("isVideo")]
        public bool IsVideo { get; set; }

        [JsonPropertyName("qualityText")]
        public string? QualityText { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("isPaused")]
        public bool IsPaused { get; set; }

        [JsonPropertyName("formatId")]
        public string? FormatId { get; set; }

        [JsonPropertyName("destinationPath")]
        public string? DestinationPath { get; set; }

        [JsonPropertyName("isNetfree")]
        public bool IsNetfree { get; set; }

        [JsonPropertyName("playlistTitle")]
        public string? PlaylistTitle { get; set; }

        [JsonPropertyName("currentState")]
        public string? CurrentState { get; set; }

        [JsonPropertyName("lastPercent")]
        public string? LastPercent { get; set; }

        [JsonPropertyName("lastSpeed")]
        public string? LastSpeed { get; set; }
    }

    public class SearchResultItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; } = string.Empty;

        [JsonPropertyName("duration")]
        public string Duration { get; set; } = string.Empty;
    }

    public class DownloadMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Query { get; set; }

        [JsonPropertyName("searchResults")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SearchResultItem>? SearchResults { get; set; }

        [JsonPropertyName("percent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Percent { get; set; }

        [JsonPropertyName("speed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Speed { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }

        [JsonPropertyName("path")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("current")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Current { get; set; }

        [JsonPropertyName("total")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Total { get; set; }

        [JsonPropertyName("downloadId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DownloadId { get; set; }

        [JsonPropertyName("successCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SuccessCount { get; set; }

        [JsonPropertyName("failureCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FailureCount { get; set; }

        [JsonPropertyName("successfulFiles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? SuccessfulFiles { get; set; }

        [JsonPropertyName("activeDownloads")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ActiveDownloadState>? ActiveDownloads { get; set; }

        [JsonPropertyName("isSingleDownloadActive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsSingleDownloadActive { get; set; }

        // Artist tracking specific payload fields
        [JsonPropertyName("artist")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TrackedArtist? Artist { get; set; }

        [JsonPropertyName("artists")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TrackedArtist>? Artists { get; set; }

        [JsonPropertyName("newSongsCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NewSongsCount { get; set; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; }

        [JsonPropertyName("totalNewSongs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TotalNewSongs { get; set; }

        public DownloadMessage() { }

        public DownloadMessage(string type)
        {
            Type = type;
        }

        public static DownloadMessage Starting(string? downloadId = null) =>
            new DownloadMessage("starting") { DownloadId = downloadId };

        public static DownloadMessage Progress(string percent, string speed, string? downloadId = null) =>
            new DownloadMessage("progress") { Percent = percent, Speed = speed, DownloadId = downloadId };

        public static DownloadMessage PlaylistProgress(string current, string total, string? downloadId = null) =>
            new DownloadMessage("playlist_progress") { Current = current, Total = total, DownloadId = downloadId };

        public static DownloadMessage Success(string path, string? downloadId = null) =>
            new DownloadMessage("success") { Path = path, DownloadId = downloadId };

        public static DownloadMessage CreateError(string errorMsg, string? downloadId = null) =>
            new DownloadMessage("error") { Error = errorMsg, DownloadId = downloadId };

        public static DownloadMessage Cancelled(string? downloadId = null) =>
            new DownloadMessage("cancelled") { DownloadId = downloadId };

        public static DownloadMessage Processing(string? downloadId = null) =>
            new DownloadMessage("processing") { DownloadId = downloadId };

        public static DownloadMessage Merging(string? downloadId = null) =>
            new DownloadMessage("merging") { DownloadId = downloadId };

        public static DownloadMessage NetfreeBlocked(string? downloadId = null) =>
            new DownloadMessage("netfree_error") { DownloadId = downloadId };

        public static DownloadMessage Metadata(string title, string thumbnail, string? downloadId = null) =>
            new DownloadMessage("metadata") { Title = title, Thumbnail = thumbnail, DownloadId = downloadId };

        public static DownloadMessage DestinationSelected(string? path) =>
            new DownloadMessage("destination_selected") { Path = path };

        public static DownloadMessage QueueComplete(int successCount, int failureCount, List<string> successfulFiles, string path) =>
            new DownloadMessage("queue_complete")
            {
                SuccessCount = successCount,
                FailureCount = failureCount,
                SuccessfulFiles = successfulFiles,
                Path = path
            };

        public static DownloadMessage RestoreState(List<ActiveDownloadState> activeDownloads, bool isSingleActive) =>
            new DownloadMessage("restore_state")
            {
                ActiveDownloads = activeDownloads,
                IsSingleDownloadActive = isSingleActive
            };
    }
}
