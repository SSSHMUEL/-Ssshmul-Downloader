using System.Text.Json.Serialization;

namespace SsshmulDownloader.Models
{
    public class AppSettings
    {
        [JsonPropertyName("customSavePath")]
        public string? CustomSavePath { get; set; }

        [JsonPropertyName("artistTrackerSavePath")]
        public string? ArtistTrackerSavePath { get; set; }

        [JsonPropertyName("savedMp3Quality")]
        public string SavedMp3Quality { get; set; } = "mp3_high";

        [JsonPropertyName("savedMp4Quality")]
        public string SavedMp4Quality { get; set; } = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

        [JsonPropertyName("defaultRegularFormatType")]
        public string DefaultRegularFormatType { get; set; } = "mp4";

        [JsonPropertyName("defaultArtistTrackerFormatType")]
        public string DefaultArtistTrackerFormatType { get; set; } = "mp3";

        [JsonPropertyName("artistTrackerMp3Quality")]
        public string ArtistTrackerMp3Quality { get; set; } = "mp3_high";

        [JsonPropertyName("artistTrackerMp4Quality")]
        public string ArtistTrackerMp4Quality { get; set; } = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

        [JsonPropertyName("preferCleanAudio")]
        public bool PreferCleanAudio { get; set; } = true;

        [JsonPropertyName("downloadSubsDefault")]
        public bool DownloadSubsDefault { get; set; } = false;

        [JsonPropertyName("subsLangDefault")]
        public string SubsLangDefault { get; set; } = "he";

        [JsonPropertyName("subsTypeDefault")]
        public string SubsTypeDefault { get; set; } = "separate";

        [JsonPropertyName("scanIntervalHours")]
        public int ScanIntervalHours { get; set; } = 6;

        [JsonPropertyName("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = true;

        [JsonPropertyName("autoStartWithWindows")]
        public bool AutoStartWithWindows { get; set; } = false;

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";
    }
}
