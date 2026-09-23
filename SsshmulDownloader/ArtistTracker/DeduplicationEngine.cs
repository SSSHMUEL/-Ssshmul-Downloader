using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SsshmulDownloader.Models;

namespace SsshmulDownloader.ArtistTracker
{
    public static class DeduplicationEngine
    {
        public static string CleanArtistName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Artist";
            string clean = name.Trim();
            clean = Regex.Replace(clean, @"(?i)\s+channel$", "");
            clean = Regex.Replace(clean, @"(?i)\s*-\s*Topic$", "");
            clean = Regex.Replace(clean, @"(?i)\s+Topic$", "");
            clean = Regex.Replace(clean, @"(?i)\s+Official$", "");
            clean = Regex.Replace(clean, @"(?i)\s+ערוץ\s*רשמי$", "");
            clean = Regex.Replace(clean, @"(?i)\s*-\s*נושא$", "");
            clean = Regex.Replace(clean, @"(?i)\s+נושא$", "");
            return clean.Trim();
        }

        public static string SanitizeFolderName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Artist";
            string cleaned = CleanArtistName(name);
            return Regex.Replace(cleaned, @"[\\/:*?""<>|]", "_").Trim();
        }

        public static string NormalizeSongTitle(string? title, string? artistName)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            string clean = title.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(artistName))
            {
                clean = clean.Replace(artistName.ToLowerInvariant(), " ");
                string sanitizedArtist = CleanArtistName(artistName).ToLowerInvariant();
                clean = clean.Replace(sanitizedArtist, " ");
            }

            // Remove YouTube suffix
            clean = Regex.Replace(clean, @"\s*-\s*youtube$", "", RegexOptions.IgnoreCase);

            // Strip video tags, brackets, and common video-only indicators
            clean = Regex.Replace(clean, @"\[(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי|אודיו|גרסת\s*אולפן|visualizer)\]", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\((?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי|אודיו|גרסת\s*אולפן|visualizer)\)", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\b(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|קליפ\s*רשמי|אודיו\s*רשמי|visualizer)\b", " ", RegexOptions.IgnoreCase);

            // Strip brackets, parentheses and dashes
            clean = Regex.Replace(clean, @"[\[\](){}\-_–|:]", " ");
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();

            return clean;
        }

        public static bool IsShortOrPromo(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string lower = title.ToLowerInvariant();
            return lower.Contains("#shorts") || lower.Contains("#short") ||
                   lower.Contains("[teaser]") || lower.Contains("(teaser)") ||
                   lower.Contains("trailer") || lower.Contains("behind the scenes") ||
                   lower.Contains("making of") || lower.Contains("טיזר") || lower.Contains("פרומו");
        }

        public static int GetSongScore(ArtistSong song)
        {
            string t = song.Title.ToLowerInvariant();
            int score = 0;

            // Highest priority: Official Audio / Studio release / Topic version
            if (t.Contains("official audio") || t.Contains("אודיו רשמי")) score += 100;
            else if (t.Contains("audio") || t.Contains("אודיו") || t.Contains("גרסת אולפן")) score += 85;
            else if (t.Contains("official lyric video") || t.Contains("lyric video")) score += 65;
            else if (t.Contains("official music video") || t.Contains("קליפ רשמי")) score += 50;
            else if (t.Contains("music video") || t.Contains("official video")) score += 40;
            else if (t.Contains("visualizer")) score += 30;
            else if (t.Contains("live") || t.Contains("acoustic") || t.Contains("הופעה")) score += 20;
            else score += 45; // Standard release

            // Penalize non-primary language translations
            if (t.Contains("spanish lyric") || t.Contains("portuguese lyric") ||
                t.Contains("french lyric") || t.Contains("german lyric") ||
                t.Contains("russian lyric"))
            {
                score -= 40;
            }

            return score;
        }

        public static List<ArtistSong> DeduplicateArtistSongs(List<ArtistSong> rawSongs, string artistName)
        {
            var bestPerTitle = new Dictionary<string, ArtistSong>(StringComparer.OrdinalIgnoreCase);

            foreach (var song in rawSongs)
            {
                if (IsShortOrPromo(song.Title)) continue;

                string cleanKey = NormalizeSongTitle(song.Title, artistName);
                if (string.IsNullOrWhiteSpace(cleanKey))
                {
                    cleanKey = song.VideoId;
                }

                int score = GetSongScore(song);
                song.IsOfficialAudio = score >= 80;

                if (!bestPerTitle.TryGetValue(cleanKey, out var existing))
                {
                    bestPerTitle[cleanKey] = song;
                }
                else
                {
                    int existingScore = GetSongScore(existing);
                    if (score > existingScore)
                    {
                        bestPerTitle[cleanKey] = song;
                    }
                }
            }

            return new List<ArtistSong>(bestPerTitle.Values);
        }
    }
}
