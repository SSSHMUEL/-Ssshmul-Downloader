using System.Text.Json.Serialization;

namespace SsshmulDownloader.Models
{
    public class TagMappings
    {
        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonPropertyName("album")]
        public string? Album { get; set; }

        [JsonPropertyName("year")]
        public string? Year { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }
    }

    public class SubsOptions
    {
        [JsonPropertyName("lang")]
        public string? Lang { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; } // "separate", "embed", "burn"

        public SubsOptions() { }

        public SubsOptions(string lang, string type)
        {
            Lang = lang;
            Type = type;
        }
    }
}
