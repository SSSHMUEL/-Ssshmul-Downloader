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

            // Strip version variations: Radio Edit, Cinematic, Acoustic, Deluxe, Remix, Slowed, Sped Up, Instrumental, Mixed, Live, Version, Stripped, Piano, Strings, Sessions, Acapella
            clean = Regex.Replace(clean, @"\[(?:radio\s*edit|cinematic|acoustic|deluxe(?:\s*mix)?|remix|club\s*mix|vip\s*mix|slowed(?:\s*\+\s*reverb)?|sped\s*up|speed\s*up|nightcore|instrumental|mixed|live(?:\s*from|\s*at|\s*in|\s*version)?|ballad|single\s*version|film\s*version|wedding\s*version|extended|unplugged|stripped|piano\s*version|strings\s*version|acapella|sessions?|village\s*sessions?|orchestral)\]", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\((?:radio\s*edit|cinematic|acoustic|deluxe(?:\s*mix)?|remix|club\s*mix|vip\s*mix|slowed(?:\s*\+\s*reverb)?|sped\s*up|speed\s*up|nightcore|instrumental|mixed|live(?:\s*from|\s*at|\s*in|\s*version)?|ballad|single\s*version|film\s*version|wedding\s*version|extended|unplugged|stripped|piano\s*version|strings\s*version|acapella|sessions?|village\s*sessions?|orchestral|from\s*the\s*netflix\s*series[^)]*|from\s*the\s*motion\s*picture[^)]*)\)", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\b(?:radio\s*edit|cinematic|acoustic|deluxe\s*mix|club\s*mix|slowed\s*reverb|sped\s*up|nightcore|live\s*from|live\s*at|live\s*in|wedding\s*version|stripped|piano\s*version|strings\s*version|acapella|village\s*sessions|גרסה\s*אקוסטית|גרסת\s*רדיו|גרסת\s*פסנתר|בהופעה\s*חיה|בהופעה)\b", " ", RegexOptions.IgnoreCase);

            // Strip featured artist suffixes like (feat. XYZ) / (with XYZ) to prevent duplicate song entries with variations
            clean = Regex.Replace(clean, @"\((?:feat\.?|featuring|with|ft\.?)\s+[^)]+\)", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\[(?:feat\.?|featuring|with|ft\.?)\s+[^\]]+\]", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\b(?:feat\.?|featuring|with|ft\.?)\s+.*$", " ", RegexOptions.IgnoreCase);

            // Strip brackets, parentheses and dashes
            clean = Regex.Replace(clean, @"[\[\](){}\-_–|:]", " ");
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();

            return clean;
        }

        public static bool IsVariantOrNonStudioTrack(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string lower = title.ToLowerInvariant();

            // Check if track is a live recording, acoustic version, remix, club mix, sped up / slowed, stripped, piano version, strings version, sessions, etc.
            if (lower.Contains("live from") || lower.Contains("live at") || lower.Contains("live in") ||
                lower.Contains("(live") || lower.Contains("[live") || lower.Contains(" live]") || lower.Contains(" live)") ||
                lower.Contains("בהופעה") || lower.Contains("הופעה חיה") || lower.Contains("מופע") ||
                lower.Contains("acoustic") || lower.Contains("אקוסטי") ||
                lower.Contains("stripped") || lower.Contains("piano version") || lower.Contains("גרסת פסנתר") ||
                lower.Contains("strings version") || lower.Contains("sessions") ||
                lower.Contains("club mix") || lower.Contains("remix") || lower.Contains("רמיקס") ||
                lower.Contains("wedding version") || lower.Contains("orchestral") ||
                lower.Contains("sped up") || lower.Contains("speed up") || lower.Contains("slowed") ||
                lower.Contains("nightcore") || lower.Contains("radio edit") || lower.Contains("cinematic") ||
                lower.Contains("acapella") || lower.Contains("אקפלה") ||
                lower.Contains("instrumental") || lower.Contains("אינסטרומנטל") || lower.Contains("unplugged") ||
                lower.Contains("cassette version") || lower.Contains("campfire version") || lower.Contains("folk version"))
            {
                return true;
            }

            return false;
        }

        public static bool IsShortOrPromo(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string lower = title.ToLowerInvariant();
            
            // Shorts, trailers, promos, teasers
            if (lower.Contains("#shorts") || lower.Contains("#short") ||
                lower.Contains("[teaser]") || lower.Contains("(teaser)") ||
                lower.Contains("trailer") || lower.Contains("behind the scenes") ||
                lower.Contains("making of") || lower.Contains("טיזר") || lower.Contains("פרומו") ||
                lower.Contains("sneak peek") || lower.Contains("preview"))
            {
                return true;
            }

            // Non-music content / vlogs / interviews / podcasts / reaction / tutorials
            if (lower.Contains("vlog") || lower.Contains("interview") || lower.Contains("ראיון") ||
                lower.Contains("פודקאסט") || lower.Contains("podcast") || lower.Contains("מאחורי הקלעים") ||
                lower.Contains("ריאקשן") || lower.Contains("reaction") || lower.Contains("unboxing") ||
                lower.Contains("q&a") || lower.Contains("שאלות ותשובות") || lower.Contains("live stream") ||
                lower.Contains("שידור חי") || lower.Contains("making the album") || lower.Contains("docuseries") ||
                lower.Contains("documentary") || lower.Contains("פרק ") || lower.Contains("episode ") ||
                lower.Contains("compilation") || lower.Contains("tiktok") || lower.Contains("tour video") ||
                lower.Contains("on tour"))
            {
                return true;
            }

            // Foreign language translated lyrics videos (e.g. Spanish lyric video, Japanese lyric video, etc.)
            if (lower.Contains("spanish lyric") || lower.Contains("japanese lyric") || lower.Contains("italian lyric") ||
                lower.Contains("polish lyric") || lower.Contains("french lyric") || lower.Contains("portuguese lyric") ||
                lower.Contains("chinese") || lower.Contains("german lyric") || lower.Contains("russian lyric") ||
                lower.Contains("sub español") || lower.Contains("legendado"))
            {
                return true;
            }

            // Covers / Tribute recordings / Karaoke / Instrumental tutorials
            if (lower.Contains("cover") || lower.Contains("קאבר") || lower.Contains("מחווה") ||
                lower.Contains("tribute") || lower.Contains("karaoke") || lower.Contains("קריוקי") ||
                lower.Contains("פלייבק") || lower.Contains("backing track") || lower.Contains("instrumental") ||
                lower.Contains("piano tutorial") || lower.Contains("guitar tutorial") || lower.Contains("how to play"))
            {
                return true;
            }

            // Full concerts, live sets, mega-mixes (overviews)
            if (lower.Contains("full concert") || lower.Contains("הופעה מלאה") || lower.Contains("מופע מלא") ||
                lower.Contains("full show") || lower.Contains("live in concert") || lower.Contains("live set") ||
                lower.Contains("dj set") || lower.Contains("mega mix") || lower.Contains("megamix") ||
                lower.Contains("מחרוזת הופעה") || lower.Contains("מחרוזת לייב"))
            {
                return true;
            }

            return false;
        }

        public static int GetSongScore(ArtistSong song)
        {
            string t = song.Title.ToLowerInvariant();
            int score = 100;

            // Big penalty for any extra version modifiers in the title (Acoustic, Radio Edit, Cinematic, Live, Remix, etc.)
            if (t.Contains("acoustic") || t.Contains("אקוסטי")) score -= 50;
            if (t.Contains("radio edit") || t.Contains("גרסת רדיו")) score -= 45;
            if (t.Contains("cinematic")) score -= 45;
            if (t.Contains("live") || t.Contains("הופעה") || t.Contains("בהופעה חיה")) score -= 50;
            if (t.Contains("remix") || t.Contains("רמיקס")) score -= 40;
            if (t.Contains("slowed") || t.Contains("sped up") || t.Contains("nightcore")) score -= 45;
            if (t.Contains("instrumental") || t.Contains("אינסטרומנטל")) score -= 50;
            if (t.Contains("deluxe") || t.Contains("ballad") || t.Contains("extended")) score -= 30;

            // Bonus for standard clean studio / official audio release
            if (t.Contains("official audio") || t.Contains("אודיו רשמי")) score += 30;
            else if (t.Contains("גרסת אולפן") || t.Contains("studio version") || t.Contains("original mix")) score += 25;
            else if (!t.Contains("(") && !t.Contains("[") && !t.Contains("-")) score += 20; // Purest cleanest title!

            // Lower priority for video clips if audio exists
            if (t.Contains("official music video") || t.Contains("music video") || t.Contains("קליפ רשמי")) score -= 15;

            // Heavily penalize foreign language translated lyrics
            if (t.Contains("spanish lyric") || t.Contains("portuguese lyric") ||
                t.Contains("french lyric") || t.Contains("german lyric") ||
                t.Contains("russian lyric") || t.Contains("polish lyric") ||
                t.Contains("japanese lyric") || t.Contains("chinese"))
            {
                score -= 80;
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
