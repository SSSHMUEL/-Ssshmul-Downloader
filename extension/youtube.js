const EXT_VERSION = 0.4;
const userLang = navigator.language.startsWith('he') ? 'he' : 'en';
const i18n = {
    en: {
        btnText: "Download",
        btnMp3Text: "Audio",
        btnMp4Text: "Video",
        btnVideoText: "Download Video",
        btnClipText: "Download Video",
        preparing: "Starting...",
        completed: "Done!",
        error: "Error",
        downloadingMp3: "🚀 Starting MP3 download in background...",
        downloadingMp4: "🚀 Starting MP4 download in background...",
        subsDownloaded: "📝 Subtitles (.srt) downloaded!",
        subsNotFound: "No subtitles found for this video",
        appNotRunning: "⚠️ Please make sure Ssshmul Downloader is open."
    },
    he: {
        btnText: "הורד",
        btnMp3Text: "שמע",
        btnMp4Text: "וידאו",
        btnVideoText: "הורד וידאו",
        btnClipText: "הורד סרטון",
        preparing: "מתחיל...",
        completed: "הושלם!",
        error: "שגיאה",
        downloadingMp3: "🚀 הורדת MP3 החלה ברקע...",
        downloadingMp4: "🚀 הורדת MP4 החלה ברקע...",
        subsDownloaded: "📝 כתוביות (SRT) ירדו בהצלחה!",
        subsNotFound: "לא נמצאו כתוביות לסרטון זה",
        appNotRunning: "⚠️ אנא ודא שתוכנת Ssshmul Downloader פתוחה במחשב."
    }
}[userLang];

let lastActiveVideo = null;
document.addEventListener('play', (e) => { if (e.target.tagName === 'VIDEO') lastActiveVideo = e.target; }, true);
document.addEventListener('loadstart', (e) => { if (e.target.tagName === 'VIDEO') lastActiveVideo = e.target; }, true);

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === 'download_ws_event' && request.data) {
        handleDownloadWsEvent(request.data);
    }

    if (request.action === 'bind_new_media' && lastActiveVideo) {
        lastActiveVideo.dataset.sniffedUrl = request.url;
    }

    if (request.action === 'get_top_metadata') {
        const ignoreTitleDomains = [
            'whatsapp.com', 'telegram.org', 'messenger.com',
            'facebook.com', 'instagram.com', 'twitter.com', 'x.com', 'ysiva.thechats.click', 'chatfree.app', 'black-cat.thechats.click', 'authenti.newsupdates.click', 'hagizra.news', 'tiktok.com'
        ];
        const isFeedSite = ignoreTitleDomains.some(domain => window.location.hostname.includes(domain));

        let topTitle = document.title;
        if (topTitle && topTitle.endsWith(" - YouTube")) {
            topTitle = topTitle.replace(" - YouTube", "").trim();
        }
        topTitle = topTitle.replace(/^\(\d+\)\s*/, "");
        if (isFeedSite) topTitle = null;

        let topThumb = null;
        const ogImage = document.querySelector('meta[property="og:image"]');
        if (ogImage && ogImage.content) topThumb = ogImage.content;

        let topChannel = '';
        const chEl = document.querySelector('ytd-watch-metadata #owner #channel-name a') ||
                     document.querySelector('#channel-name a') ||
                     document.querySelector('ytd-channel-name a') ||
                     document.querySelector('#owner-name a');
        if (chEl && chEl.textContent) {
            topChannel = chEl.textContent.trim();
        }

        sendResponse({ title: topTitle, thumbnail: topThumb, channel: topChannel });
    }
});

let userConfig = { btnStyle: 'classic', enableGenericFloatingBtn: false };

chrome.storage.local.get(['userConfig'], (result) => {
    if (result.userConfig) {
        userConfig = { ...userConfig, ...result.userConfig };
    }
});

// Toast notification helper
function showToast(message, isWarning = false) {
    let container = document.getElementById('nf-toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'nf-toast-container';
        container.style.cssText = `
            position: fixed;
            bottom: 24px;
            right: 24px;
            z-index: 2147483647;
            display: flex;
            flex-direction: column;
            gap: 10px;
            direction: rtl;
            pointer-events: none;
            font-family: 'Rubik', 'Segoe UI', Arial, sans-serif;
        `;
        document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.style.cssText = `
        background: ${isWarning ? '#d35400' : '#1e293b'};
        color: #fff;
        padding: 12px 20px;
        border-radius: 10px;
        font-size: 14px;
        font-weight: 500;
        box-shadow: 0 8px 24px rgba(0,0,0,0.4);
        border: 1px solid ${isWarning ? '#e67e22' : '#334155'};
        opacity: 0;
        transform: translateY(20px);
        transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
        pointer-events: auto;
        display: flex;
        align-items: center;
        gap: 8px;
    `;
    toast.textContent = message;
    container.appendChild(toast);

    requestAnimationFrame(() => {
        toast.style.opacity = '1';
        toast.style.transform = 'translateY(0)';
    });

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(10px)';
        setTimeout(() => toast.remove(), 300);
    }, 4500);
}

// Subtitles (SRT) Extractor & Downloader
function formatSrtTime(ms) {
    const totalSec = Math.floor(ms / 1000);
    const mmm = String(Math.floor(ms % 1000)).padStart(3, '0');
    const ss = String(totalSec % 60).padStart(2, '0');
    const mm = String(Math.floor((totalSec / 60) % 60)).padStart(2, '0');
    const hh = String(Math.floor(totalSec / 3600)).padStart(2, '0');
    return `${hh}:${mm}:${ss},${mmm}`;
}

function parseSubtitleDataToEntries(rawText) {
    const entries = [];
    if (!rawText || typeof rawText !== 'string') return entries;
    const trimmed = rawText.trim();

    // 1. Try JSON3 format
    if (trimmed.startsWith('{')) {
        try {
            const jsonData = JSON.parse(trimmed);
            if (jsonData && Array.isArray(jsonData.events)) {
                for (const ev of jsonData.events) {
                    if (!ev.segs || ev.segs.length === 0) continue;
                    const startMs = ev.tStartMs || 0;
                    const durationMs = ev.dDurationMs || 0;
                    const endMs = startMs + durationMs;
                    const text = ev.segs.map(s => s.utf8 || "").join("").replace(/\n+/g, ' ').trim();
                    if (text) {
                        entries.push({ startMs, endMs, text });
                    }
                }
                if (entries.length > 0) return entries;
            }
        } catch (e) { }
    }

    // 2. Try XML / TTML format
    try {
        const parser = new DOMParser();
        const doc = parser.parseFromString(trimmed, "text/xml");

        // Format A: <text start="1.5" dur="3.0">hello</text>
        const textNodes = doc.querySelectorAll('text');
        if (textNodes && textNodes.length > 0) {
            textNodes.forEach(node => {
                const startMs = parseFloat(node.getAttribute('start') || '0') * 1000;
                const durMs = parseFloat(node.getAttribute('dur') || '0') * 1000;
                const endMs = startMs + durMs;
                let text = (node.textContent || '').replace(/\n+/g, ' ').trim();
                text = text.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'");
                if (text) {
                    entries.push({ startMs, endMs, text });
                }
            });
            if (entries.length > 0) return entries;
        }

        // Format B: <p t="1500" d="3000"><s>hello</s></p>
        const pNodes = doc.querySelectorAll('p');
        if (pNodes && pNodes.length > 0) {
            pNodes.forEach(node => {
                const startMs = parseFloat(node.getAttribute('t') || node.getAttribute('begin') || '0');
                const durMs = parseFloat(node.getAttribute('d') || '0');
                const endMs = startMs + durMs;
                let text = (node.textContent || '').replace(/\n+/g, ' ').trim();
                if (text) {
                    entries.push({ startMs, endMs, text });
                }
            });
            if (entries.length > 0) return entries;
        }
    } catch (e) { }

    // 3. Try WebVTT format
    if (trimmed.includes('-->')) {
        try {
            const vttRegex = /(?:(\d{2}):)?(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(?:(\d{2}):)?(\d{2}):(\d{2})[.,](\d{3})[^\r\n]*\r?\n([\s\S]*?)(?=(?:\r?\n\r?\n|$))/g;
            let match;
            while ((match = vttRegex.exec(trimmed)) !== null) {
                const parseTime = (h, m, s, ms) => ((parseInt(h || '0', 10) * 3600) + (parseInt(m, 10) * 60) + parseInt(s, 10)) * 1000 + parseInt(ms, 10);
                const startMs = parseTime(match[1], match[2], match[3], match[4]);
                const endMs = parseTime(match[5], match[6], match[7], match[8]);
                const text = (match[9] || '').replace(/<[^>]+>/g, '').replace(/\n+/g, ' ').trim();
                if (text) {
                    entries.push({ startMs, endMs, text });
                }
            }
            if (entries.length > 0) return entries;
        } catch (e) { }
    }

    return entries;
}

function buildSrtFromEntries(entries) {
    if (!entries || entries.length === 0) return "";
    let srt = "";
    let validIndex = 1;
    entries.forEach((item) => {
        if (item.text && item.text.trim()) {
            srt += `${validIndex}\n${formatSrtTime(item.startMs)} --> ${formatSrtTime(item.endMs)}\n${item.text.trim()}\n\n`;
            validIndex++;
        }
    });
    return srt;
}

function extractJsonFromText(text, marker) {
    if (!text) return null;
    const idx = text.indexOf(marker);
    if (idx === -1) return null;
    const start = text.indexOf('{', idx);
    if (start === -1) return null;
    let depth = 0;
    let inString = false;
    let escape = false;
    for (let i = start; i < text.length; i++) {
        const char = text[i];
        if (escape) { escape = false; continue; }
        if (char === '\\') { escape = true; continue; }
        if (char === '"') { inString = !inString; continue; }
        if (!inString) {
            if (char === '{') depth++;
            else if (char === '}') {
                depth--;
                if (depth === 0) {
                    try {
                        return JSON.parse(text.substring(start, i + 1));
                    } catch (e) { return null; }
                }
            }
        }
    }
    return null;
}

async function fetchAndDownloadSubtitles(videoTitle) {
    try {
        const storage = await new Promise(r => chrome.storage.local.get(['subsLangDefault'], r));
        const prefLang = storage?.subsLangDefault || 'he';

        const videoIdMatch = window.location.href.match(/[?&]v=([^&]+)/) || window.location.href.match(/youtu\.be\/([^?]+)/);
        const videoId = videoIdMatch ? videoIdMatch[1] : null;

        let captionTracks = null;

        // Strategy 1: YouTube Internal Player API (Most reliable in 2025/2026)
        if (videoId) {
            try {
                const apiResp = await fetch('https://www.youtube.com/youtubei/v1/player?prettyPrint=false', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        videoId: videoId,
                        context: {
                            client: {
                                clientName: 'WEB',
                                clientVersion: '2.20240101.01.00',
                                hl: 'he',
                                gl: 'IL'
                            }
                        }
                    })
                });
                if (apiResp.ok) {
                    const apiData = await apiResp.json();
                    captionTracks = apiData?.captions?.playerCaptionsTracklistRenderer?.captionTracks;
                }
            } catch (e) { }
        }

        // Strategy 2: DOM Script Tags with balanced JSON extraction
        if (!captionTracks || captionTracks.length === 0) {
            const scripts = Array.from(document.querySelectorAll('script'));
            for (const s of scripts) {
                const text = s.textContent || "";
                if (text.includes('captionTracks')) {
                    const parsed = extractJsonFromText(text, 'ytInitialPlayerResponse') || extractJsonFromText(text, 'playerResponse');
                    if (parsed?.captions?.playerCaptionsTracklistRenderer?.captionTracks) {
                        captionTracks = parsed.captions.playerCaptionsTracklistRenderer.captionTracks;
                        break;
                    }
                }
            }
        }

        // Strategy 3: Direct page fetch with balanced JSON extraction
        if (!captionTracks || captionTracks.length === 0) {
            try {
                const pageResp = await fetch(window.location.href, {
                    headers: { 'Accept-Language': 'he,en;q=0.9' }
                });
                if (pageResp.ok) {
                    const html = await pageResp.text();
                    const parsed = extractJsonFromText(html, 'ytInitialPlayerResponse') || extractJsonFromText(html, 'var ytInitialPlayerResponse');
                    captionTracks = parsed?.captions?.playerCaptionsTracklistRenderer?.captionTracks;
                }
            } catch (e) { }
        }

        let srtContent = "";

        // Strategy 4: Direct TimedText API queries if captionTracks wasn't found
        if ((!captionTracks || captionTracks.length === 0) && videoId) {
            const directTimedTextUrls = [
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=he&fmt=json3`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=iw&fmt=json3`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en&tlang=he&fmt=json3`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en&tlang=he&fmt=vtt`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en&tlang=he`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en&fmt=json3`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en&fmt=vtt`,
                `https://www.youtube.com/api/timedtext?v=${videoId}&lang=en`
            ];

            for (const url of directTimedTextUrls) {
                try {
                    const r = await fetch(url);
                    if (r.ok) {
                        const txt = await r.text();
                        const parsed = parseSubtitleDataToEntries(txt);
                        if (parsed && parsed.length > 0) {
                            srtContent = buildSrtFromEntries(parsed);
                            if (srtContent) break;
                        }
                    }
                } catch (e) { }
            }
        }

        if (!srtContent && captionTracks && captionTracks.length > 0) {
            const heTrack = captionTracks.find(t => 
                (t.languageCode && (t.languageCode === 'he' || t.languageCode === 'iw')) ||
                (t.vssId && (t.vssId.includes('.he') || t.vssId.includes('.iw')))
            );
            const enTrack = captionTracks.find(t => t.languageCode && t.languageCode.startsWith('en')) || captionTracks[0];

            const fetchTrackEntries = async (track, targetLang = null) => {
                if (!track || !track.baseUrl) return [];
                const baseUrlWithoutFmt = track.baseUrl.replace(/&fmt=[^&]+/g, '');
                const urlsToTry = [
                    targetLang ? `${baseUrlWithoutFmt}&tlang=${targetLang}&fmt=json3` : `${baseUrlWithoutFmt}&fmt=json3`,
                    targetLang ? `${baseUrlWithoutFmt}&tlang=${targetLang}&fmt=vtt` : `${baseUrlWithoutFmt}&fmt=vtt`,
                    targetLang ? `${baseUrlWithoutFmt}&tlang=${targetLang}` : baseUrlWithoutFmt
                ];

                for (const u of urlsToTry) {
                    try {
                        const r = await fetch(u);
                        if (r.ok) {
                            const txt = await r.text();
                            const parsed = parseSubtitleDataToEntries(txt);
                            if (parsed && parsed.length > 0) return parsed;
                        }
                    } catch (e) { }
                }
                return [];
            };

            if (prefLang === 'he,en') {
                const enEntries = await fetchTrackEntries(enTrack);
                let heEntries = [];
                if (heTrack) {
                    heEntries = await fetchTrackEntries(heTrack);
                }
                if (heEntries.length === 0 && enTrack) {
                    heEntries = await fetchTrackEntries(enTrack, 'he');
                }

                if (enEntries.length > 0) {
                    const dualEntries = enEntries.map((enItem, idx) => {
                        const heItem = heEntries[idx];
                        const heText = heItem ? heItem.text : '';
                        const combined = (heText && heText !== enItem.text)
                            ? `${heText}\n${enItem.text}`
                            : enItem.text;
                        return {
                            startMs: enItem.startMs,
                            endMs: enItem.endMs,
                            text: combined
                        };
                    });
                    srtContent = buildSrtFromEntries(dualEntries);
                }
            } else if (prefLang === 'en') {
                const enEntries = await fetchTrackEntries(enTrack);
                if (enEntries.length > 0) {
                    srtContent = buildSrtFromEntries(enEntries);
                }
            } else {
                // Hebrew (default)
                let heEntries = [];
                if (heTrack) {
                    heEntries = await fetchTrackEntries(heTrack);
                }
                if (heEntries.length === 0 && enTrack) {
                    heEntries = await fetchTrackEntries(enTrack, 'he');
                }

                if (heEntries.length > 0) {
                    srtContent = buildSrtFromEntries(heEntries);
                } else if (enTrack) {
                    // Fallback to English
                    const enEntries = await fetchTrackEntries(enTrack);
                    if (enEntries.length > 0) {
                        srtContent = buildSrtFromEntries(enEntries);
                    }
                }
            }
        }

        if (!srtContent || srtContent.trim().length === 0) {
            console.warn("Subtitle extraction resulted in empty content");
            return false;
        }

        const safeTitle = (videoTitle || document.title || 'subtitles').replace(/[\\/:*?"<>|]/g, '_').trim();
        
        // 1. Send to background script for chrome.downloads.download API
        chrome.runtime.sendMessage({
            action: 'save_subtitles',
            title: safeTitle,
            content: srtContent
        });

        // 2. Direct browser file download fallback
        try {
            const blob = new Blob([srtContent], { type: 'text/plain;charset=utf-8' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = `${safeTitle}.srt`;
            document.body.appendChild(a);
            a.click();
            setTimeout(() => {
                URL.revokeObjectURL(a.href);
                a.remove();
            }, 1000);
        } catch (err) { }

        return true;
    } catch (e) {
        console.error('Error fetching in-browser subtitles:', e);
        return false;
    }
}

function getTargetUrlAndMetadata(isNetube, btn) {
    if (isNetube) {
        let url = window.location.href;
        const ytBtn = Array.from(document.querySelectorAll('a, button')).find(el => el.textContent && el.textContent.includes('פתח ב-YouTube'));
        if (ytBtn && ytBtn.tagName === 'A' && ytBtn.href.includes('youtube.com')) {
            url = ytBtn.href;
        } else {
            const iframe = document.querySelector('iframe[src*="youtube.com/embed/"]');
            if (iframe) url = `https://www.youtube.com/watch?v=${iframe.src.split('embed/')[1].split('?')[0]}`;
        }
        return { url: url, directUrl: null, title: document.title };
    }

    const isYouTube = window.location.hostname.includes('youtube.com') || window.location.hostname.includes('youtube-nocookie.com');
    if (isYouTube) {
        let url = window.location.href;

        // Check if button is inside a YouTube Shorts reel
        const reel = btn ? btn.closest('ytd-reel-video-renderer') : document.querySelector('ytd-reel-video-renderer[is-active], ytd-reel-video-renderer');
        if (reel && (window.location.pathname.includes('/shorts/') || reel.querySelector('a[href*="/shorts/"]'))) {
            const shortLink = reel.querySelector('a[href*="/shorts/"]');
            if (shortLink && shortLink.href) {
                url = shortLink.href;
            } else if (window.location.pathname.includes('/shorts/')) {
                url = window.location.href;
            }

            const titleEl = reel.querySelector('.title, #title, #headline, h2.ytd-reel-player-header-renderer, .ytd-reel-player-header-renderer .title, ytd-reel-player-header-renderer #title, yt-formatted-string.ytd-reel-player-header-renderer');
            let titleToUse = titleEl ? titleEl.textContent.trim() : document.title;
            if (titleToUse && titleToUse.endsWith(" - YouTube")) {
                titleToUse = titleToUse.replace(" - YouTube", "").trim();
            }
            titleToUse = titleToUse.replace(/^\(\d+\)\s*/, "");

            let channelName = '';
            const chEl = reel.querySelector('#channel-name a, .channel-name a, ytd-channel-name a, #owner-name a, ytd-reel-player-header-renderer #channel-name a, ytd-reel-player-header-renderer #channel-name');
            if (chEl && chEl.textContent) {
                channelName = chEl.textContent.trim();
            }

            let thumbnailUrl = null;
            let videoIdMatch = url.match(/\/shorts\/([a-zA-Z0-9_-]+)/) || url.match(/[?&]v=([^&]+)/) || url.match(/youtu\.be\/([^?]+)/);
            if (videoIdMatch && videoIdMatch[1]) {
                thumbnailUrl = `https://i.ytimg.com/vi/${videoIdMatch[1]}/hqdefault.jpg`;
            }

            return { url: url, directUrl: null, title: titleToUse, thumbnail: thumbnailUrl, channel: channelName };
        }

        if (url.includes('/embed/')) {
            url = `https://www.youtube.com/watch?v=${url.split('/embed/')[1].split('?')[0]}`;
        }
        let titleToUse = document.title;
        if (titleToUse && titleToUse.endsWith(" - YouTube")) {
            titleToUse = titleToUse.replace(" - YouTube", "").trim();
        }
        titleToUse = titleToUse.replace(/^\(\d+\)\s*/, "");
        let thumbnailUrl = null;
        let videoIdMatch = url.match(/[?&]v=([^&]+)/) || url.match(/\/shorts\/([^?&]+)/) || url.match(/youtu\.be\/([^?]+)/);
        if (videoIdMatch && videoIdMatch[1]) {
            thumbnailUrl = `https://i.ytimg.com/vi/${videoIdMatch[1]}/hqdefault.jpg`;
        }

        let channelName = '';
        const chEl = document.querySelector('ytd-watch-metadata #owner #channel-name a') ||
                     document.querySelector('#channel-name a') ||
                     document.querySelector('ytd-channel-name a') ||
                     document.querySelector('#owner-name a');
        if (chEl && chEl.textContent) {
            channelName = chEl.textContent.trim();
        }

        return { url: url, directUrl: null, title: titleToUse, thumbnail: thumbnailUrl, channel: channelName };
    }

    let videoEl = btn ? btn.videoRef : document.querySelector('video');
    let directUrl = null;

    if (videoEl) {
        if (videoEl.src && !videoEl.src.startsWith('blob:')) {
            directUrl = videoEl.src;
        } else {
            let sources = videoEl.querySelectorAll('source');
            for (let sourceEl of sources) {
                if (sourceEl && sourceEl.src && !sourceEl.src.startsWith('blob:')) {
                    directUrl = sourceEl.src;
                    if (directUrl.includes('.m3u8') || directUrl.includes('.mp4')) {
                        break;
                    }
                }
            }
        }
        if (!directUrl) {
            for (let attr of ['data-src', 'data-video-url', 'data-video', 'data-url']) {
                let val = videoEl.getAttribute(attr);
                if (val && !val.startsWith('blob:')) {
                    directUrl = val;
                    break;
                }
            }
        }
    }

    let titleToUse = document.title;
    let thumbnailUrl = null;
    const ogImage = document.querySelector('meta[property="og:image"]');
    if (ogImage && ogImage.content) {
        thumbnailUrl = ogImage.content;
    } else if (videoEl && videoEl.poster) {
        thumbnailUrl = videoEl.poster;
    }

    return { url: window.location.href, directUrl: directUrl, title: titleToUse, thumbnail: thumbnailUrl, channel: '' };
}

// Trigger direct download action
function executeDownload(format, isNetube, btn, customDownloadId) {
    const data = getTargetUrlAndMetadata(isNetube, btn);

    chrome.storage.local.get(['downloadSubsDefault'], (res) => {
        if (res.downloadSubsDefault === true || res.downloadSubsDefault === 'true') {
            fetchAndDownloadSubtitles(data.title).then(downloaded => {
                if (downloaded) {
                    showToast(i18n.subsDownloaded);
                } else {
                    showToast(i18n.subsNotFound, true);
                }
            });
        }
    });

    chrome.runtime.sendMessage({
        action: 'direct_download',
        downloadId: customDownloadId || null,
        format: format,
        url: data.url,
        directUrl: data.directUrl,
        title: data.title,
        thumbnail: data.thumbnail,
        channel: data.channel || ''
    }, (res) => {
        if (!res || !res.success) {
            if (btn && btn.resetState) {
                const iconSpan = btn.querySelector('.nf-btn-icon');
                const textSpan = btn.querySelector('.nf-btn-text');
                btn.classList.remove('is-starting', 'is-downloading');
                btn.classList.add('is-error');
                if (iconSpan) iconSpan.innerHTML = `<svg viewBox="0 0 24 24" width="20" height="20" fill="#e74c3c" focusable="false" style="display:block;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>`;
                if (textSpan) textSpan.textContent = i18n.error;
                setTimeout(() => {
                    btn.resetState();
                }, 3500);
            }
        }
    });
}

function findYouTubeActionBar() {
    const selectors = [
        'ytd-watch-metadata ytd-menu-renderer #top-level-buttons-computed',
        'ytd-watch-metadata #actions #top-level-buttons-computed',
        '#actions ytd-menu-renderer #top-level-buttons-computed',
        '#top-level-buttons-computed',
        'ytd-menu-renderer #top-level-buttons-computed',
        'ytd-playlist-header-renderer ytd-menu-renderer #top-level-buttons-computed',
        '#actions-inner #top-level-buttons-computed',
        '#actions-inner',
        '#owner #subscribe-button',
        'ytd-watch-metadata #actions'
    ];
    for (const sel of selectors) {
        const el = document.querySelector(sel);
        if (el) return el;
    }
    return null;
}

// Global format dropdown menu attached to document.body (immune to parent overflow:hidden)
let globalMenu = null;
let currentActiveBtn = null;

function getOrCreateGlobalMenu() {
    if (globalMenu && document.body.contains(globalMenu)) return globalMenu;

    const isLight = document.documentElement.getAttribute('dark') === null && (document.body.classList.contains('light-theme') || window.matchMedia('(prefers-color-scheme: light)').matches);

    globalMenu = document.createElement('div');
    globalMenu.id = 'nf-format-dropdown';
    globalMenu.style.cssText = `
        position: fixed;
        background: ${isLight ? '#ffffff' : '#282828'};
        color: ${isLight ? '#0f0f0f' : '#f1f1f1'};
        border: 1px solid ${isLight ? 'rgba(0,0,0,0.1)' : 'rgba(255,255,255,0.1)'};
        border-radius: 12px;
        box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
        padding: 6px;
        display: none;
        flex-direction: column;
        gap: 2px;
        min-width: 170px;
        z-index: 2147483647;
        direction: rtl;
        font-family: "Roboto", "YouTube Sans", system-ui, sans-serif;
    `;

    const mp3Option = document.createElement('button');
    mp3Option.innerHTML = `${i18n.btnMp3Text}`;
    mp3Option.style.cssText = `
        background: transparent;
        color: inherit;
        border: none;
        border-radius: 8px;
        padding: 9px 12px;
        font-size: 13.5px;
        font-weight: 500;
        text-align: right;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: flex-start;
        gap: 8px;
        transition: background 0.15s;
        width: 100%;
        box-sizing: border-box;
    `;
    mp3Option.onmouseover = () => { mp3Option.style.background = isLight ? 'rgba(0,0,0,0.08)' : 'rgba(255,255,255,0.12)'; };
    mp3Option.onmouseout = () => { mp3Option.style.background = 'transparent'; };

    const mp4Option = document.createElement('button');
    mp4Option.innerHTML = `${i18n.btnMp4Text}`;
    mp4Option.style.cssText = mp3Option.style.cssText;
    mp4Option.onmouseover = () => { mp4Option.style.background = isLight ? 'rgba(0,0,0,0.08)' : 'rgba(255,255,255,0.12)'; };
    mp4Option.onmouseout = () => { mp4Option.style.background = 'transparent'; };

    mp3Option.onclick = (e) => {
        e.preventDefault();
        e.stopPropagation();
        globalMenu.style.display = 'none';
        const isNetube = window.location.hostname.includes('netube.co.il');
        executeDownload('mp3', isNetube, currentActiveBtn);
    };

    mp4Option.onclick = (e) => {
        e.preventDefault();
        e.stopPropagation();
        globalMenu.style.display = 'none';
        const isNetube = window.location.hostname.includes('netube.co.il');
        executeDownload('mp4', isNetube, currentActiveBtn);
    };

    globalMenu.appendChild(mp3Option);
    globalMenu.appendChild(mp4Option);

    document.body.appendChild(globalMenu);

    document.addEventListener('click', (e) => {
        if (globalMenu && globalMenu.style.display === 'flex') {
            if (!globalMenu.contains(e.target) && (!currentActiveBtn || !currentActiveBtn.contains(e.target))) {
                globalMenu.style.display = 'none';
            }
        }
    });

    window.addEventListener('scroll', () => {
        if (globalMenu) globalMenu.style.display = 'none';
    }, { passive: true });

    return globalMenu;
}

function injectShortsButtons() {
    const targetActionBars = new Set();

    // 1. Find all action containers across various YouTube Shorts layouts
    const actionContainers = document.querySelectorAll(
        'ytd-reel-player-overlay-renderer #actions, ' +
        'ytd-reel-video-renderer #actions, ' +
        '#actions.ytd-reel-player-overlay-renderer, ' +
        'ytd-shorts #actions, ' +
        '#shorts-player #actions, ' +
        '#shorts-container #actions, ' +
        'ytd-reel-player-overlay-renderer #actions-inner, ' +
        'ytd-reel-video-renderer #actions-inner, ' +
        'ytd-reel-player-overlay-renderer .ytd-reel-player-overlay-renderer#actions'
    );
    actionContainers.forEach(el => targetActionBars.add(el));

    // 2. Find like buttons directly in shorts to guarantee finding their parent action container
    const likeButtons = document.querySelectorAll(
        'ytd-reel-video-renderer ytd-like-button-renderer, ' +
        'ytd-reel-video-renderer like-button-view-model, ' +
        'ytd-reel-video-renderer #like-button, ' +
        'ytd-reel-player-overlay-renderer ytd-like-button-renderer, ' +
        'ytd-reel-player-overlay-renderer like-button-view-model, ' +
        'ytd-reel-player-overlay-renderer #like-button, ' +
        'ytd-shorts ytd-like-button-renderer, ' +
        'ytd-shorts like-button-view-model, ' +
        'ytd-shorts #like-button, ' +
        '#shorts-container ytd-like-button-renderer, ' +
        '#shorts-container like-button-view-model, ' +
        'ytd-reel-video-renderer [aria-label*="אהבתי"], ' +
        'ytd-reel-video-renderer [aria-label*="like" i]'
    );
    likeButtons.forEach(likeBtn => {
        let parent = likeBtn.parentElement;
        while (parent && parent.tagName !== 'YTD-REEL-VIDEO-RENDERER' && parent.tagName !== 'YTD-REEL-PLAYER-OVERLAY-RENDERER') {
            if (parent.id === 'actions' || parent.classList.contains('ytd-reel-player-overlay-renderer') || parent.children.length > 2) {
                targetActionBars.add(parent);
                break;
            }
            parent = parent.parentElement;
        }
        if (likeBtn.parentElement) {
            targetActionBars.add(likeBtn.parentElement);
        }
    });

    targetActionBars.forEach(actions => {
        if (!actions) return;
        if (actions.querySelector('.nf-shorts-download-container')) return;

        // Verify this is indeed an action bar in Shorts
        const hasShortsElement = actions.closest('ytd-reel-video-renderer') ||
                                 actions.closest('ytd-reel-player-overlay-renderer') ||
                                 actions.closest('ytd-shorts') ||
                                 window.location.pathname.includes('/shorts/');
        if (!hasShortsElement) return;

        const shortsContainer = document.createElement('div');
        shortsContainer.className = 'nf-shorts-download-container style-scope ytd-reel-player-overlay-renderer';
        shortsContainer.style.cssText = `
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            margin-bottom: 16px;
            margin-top: 0;
            user-select: none;
            z-index: 1000;
        `;

        const btn = document.createElement('button');
        btn.className = 'nf-shorts-download-btn yt-spec-button-shape-next yt-spec-button-shape-next--tonal yt-spec-button-shape-next--mono yt-spec-button-shape-next--size-l yt-spec-button-shape-next--icon-button';
        btn.setAttribute('aria-label', i18n.btnText || 'הורדה');
        btn.title = i18n.btnText || 'הורדה';
        btn.style.cssText = `
            width: 48px;
            height: 48px;
            min-width: 48px;
            min-height: 48px;
            border-radius: 50%;
            border: none;
            background-color: var(--yt-spec-badge-chip-background, rgba(255, 255, 255, 0.2));
            color: var(--yt-spec-text-primary, #ffffff);
            display: flex;
            align-items: center;
            justify-content: center;
            cursor: pointer;
            transition: background-color 0.2s, transform 0.15s;
            box-sizing: border-box;
            padding: 0;
            outline: none;
            margin: 0;
            backdrop-filter: blur(8px);
            -webkit-backdrop-filter: blur(8px);
        `;

        btn.innerHTML = `
            <svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" focusable="false" style="pointer-events: none; display: block; width: 24px; height: 24px;">
                <path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"></path>
            </svg>
        `;

        btn.onmouseover = () => {
            btn.style.backgroundColor = 'var(--yt-spec-button-chip-background-hover, rgba(255, 255, 255, 0.3))';
            btn.style.transform = 'scale(1.05)';
        };
        btn.onmouseout = () => {
            btn.style.backgroundColor = 'var(--yt-spec-badge-chip-background, rgba(255, 255, 255, 0.2))';
            btn.style.transform = 'scale(1)';
        };
        btn.onmousedown = () => {
            btn.style.transform = 'scale(0.95)';
        };
        btn.onmouseup = () => {
            btn.style.transform = 'scale(1.05)';
        };

        // Directly download video immediately on click
        btn.onclick = (e) => {
            e.preventDefault();
            e.stopPropagation();
            btn.style.transform = 'scale(0.9)';
            setTimeout(() => { btn.style.transform = 'scale(1)'; }, 150);
            executeDownload('mp4', false, btn);
        };

        const label = document.createElement('span');
        label.className = 'nf-shorts-label';
        label.textContent = userLang === 'he' ? 'הורדה' : 'Download';
        label.style.cssText = `
            font-family: "Roboto", "YouTube Sans", system-ui, sans-serif;
            font-size: 12px;
            font-weight: 400;
            line-height: 1.4;
            color: var(--yt-spec-text-primary, #ffffff);
            margin-top: 6px;
            text-shadow: 0 1px 2px rgba(0,0,0,0.6);
            pointer-events: none;
            text-align: center;
            white-space: nowrap;
        `;

        shortsContainer.appendChild(btn);
        shortsContainer.appendChild(label);

        // Position ABOVE the like button
        const likeBtn = actions.querySelector(
            'ytd-like-button-renderer, like-button-view-model, #like-button, ' +
            '[aria-label*="like" i], [aria-label*="אהבתי" i], ytd-toggle-button-renderer'
        ) || actions.firstElementChild;

        if (likeBtn && likeBtn.parentElement === actions) {
            actions.insertBefore(shortsContainer, likeBtn);
        } else {
            actions.prepend(shortsContainer);
        }
    });
}

function tryInjectButton() {
    const currentHost = window.location.hostname;
    const isYouTube = currentHost.includes('youtube.com') || currentHost.includes('youtube-nocookie.com');
    const isYouTubeEmbed = isYouTube && window.location.pathname.includes('/embed/');
    const isNetube = currentHost.includes('netube.co.il');

    const unsupportedDomains = [
        'chat.google.com',
        'mail.google.com',
        'web.whatsapp.com',
        'web.telegram.org'
    ];
    if (unsupportedDomains.some(domain => currentHost.includes(domain))) return;
    if (isYouTubeEmbed) return;

    if (isYouTube) {
        injectShortsButtons();
    }

    let isGenericSite = false;
    let actionBar = null;
    let videoEl = null;

    if (isNetube) {
        actionBar = document.querySelector('.video-page-actions');
    } else if (isYouTube) {
        actionBar = findYouTubeActionBar();
    } else {
        isGenericSite = true;
        let injectedAny = false;
        const ytIframes = document.querySelectorAll('iframe[src*="youtube.com/embed/"], iframe[src*="youtube-nocookie.com/embed/"]');
        ytIframes.forEach(iframe => {
            if (iframe.parentElement && !iframe.parentElement.querySelector('.nf-iframe-btn-container')) {
                injectIframeButton(iframe);
                injectedAny = true;
            }
        });
        if (userConfig.enableGenericFloatingBtn) {
            const videos = document.querySelectorAll('video');
            videos.forEach(vid => {
                if ((vid.offsetWidth > 0 || vid.offsetHeight > 0 || vid.readyState > 0) && !vid.parentElement.querySelector('.nf-generic-btn')) {
                    injectSingleGenericButton(vid);
                    injectedAny = true;
                }
            });
        }
        if (!injectedAny) return;
        return;
    }

    if (!actionBar || document.querySelector('#nf-download-btn') || document.querySelector('#nf-download-buttons')) return;

    // Bold Solid YouTube native SVG icons
    const ytDownloadIcon = `<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" focusable="false" style="pointer-events: none; display: block; width: 24px; height: 24px;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zm-14 9v2h14v-2H5z"/></svg>`;
    const videoIcon = ytDownloadIcon;
    const audioIcon = ytDownloadIcon;
    const downloadIcon = ytDownloadIcon;
    const animArrowIcon = `<svg class="nf-anim-arrow" viewBox="0 0 24 24" width="24" height="24" fill="currentColor" focusable="false" style="pointer-events: none; display: block; width: 24px; height: 24px;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zm-14 9v2h14v-2H5z"/></svg>`;
    const checkIcon = `<svg viewBox="0 0 24 24" width="24" height="24" fill="#2ba640" focusable="false" style="pointer-events: none; display: block; width: 24px; height: 24px;"><path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/></svg>`;
    const errorIcon = `<svg viewBox="0 0 24 24" width="24" height="24" fill="#e74c3c" focusable="false" style="pointer-events: none; display: block; width: 24px; height: 24px;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>`;

    ensureYtStyles();

    if (isNetube) {
        const mainBtn = document.createElement('button');
        mainBtn.id = 'nf-download-btn';
        mainBtn.videoRef = videoEl;
        mainBtn.innerHTML = `${downloadIcon}<span style="white-space: nowrap; font-weight: 500;">${i18n.btnText}</span>`;
        mainBtn.style.cssText = `background-color: var(--bg-card, #1E293B); color: var(--text, #F1F5F9); border: 1px solid var(--border, #334155); padding: 0 16px; height: 36px; border-radius: 18px; cursor: pointer; font-family: Roboto, Rubik, Arial, sans-serif; font-size: 14px; display: inline-flex; align-items: center; justify-content: center; gap: 6px; margin: 0; transition: all 0.2s ease; box-sizing: border-box;`;
        
        mainBtn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();

            const menu = getOrCreateGlobalMenu();
            currentActiveBtn = mainBtn;

            const isVisible = menu.style.display === 'flex';
            if (isVisible) {
                menu.style.display = 'none';
            } else {
                const rect = mainBtn.getBoundingClientRect();
                menu.style.top = `${rect.bottom + 6}px`;
                const leftPos = Math.max(10, rect.right - 180);
                menu.style.left = `${leftPos}px`;
                menu.style.display = 'flex';
            }
        });

        const channelAvatar = actionBar.querySelector('#video-ch-avatar-main');
        if (channelAvatar) channelAvatar.after(mainBtn); else actionBar.prepend(mainBtn);
    } else {
        // YouTube native style button container (Only Video & Audio buttons)
        const btnContainer = document.createElement('div');
        btnContainer.id = 'nf-download-buttons';
        btnContainer.style.cssText = `display: inline-flex; align-items: center; gap: 8px; margin-inline-start: 4px; margin-inline-end: 4px; height: 38px; vertical-align: middle; flex-shrink: 0;`;

        function createYtNativeButton(defaultIconSvg, defaultText, format) {
            const btn = document.createElement('button');
            btn.className = `nf-yt-btn yt-spec-button-shape-next yt-spec-button-shape-next--tonal yt-spec-button-shape-next--mono yt-spec-button-shape-next--size-m yt-spec-button-shape-next--icon-leading`;
            btn.setAttribute('data-format', format);
            btn.title = `${defaultText} (${format.toUpperCase()})`;

            const progressFill = document.createElement('div');
            progressFill.className = 'nf-progress-fill';

            const content = document.createElement('span');
            content.className = 'nf-btn-content';

            const iconSpan = document.createElement('span');
            iconSpan.className = 'nf-btn-icon';
            iconSpan.innerHTML = defaultIconSvg;

            const textSpan = document.createElement('span');
            textSpan.className = 'nf-btn-text';
            textSpan.textContent = defaultText;

            content.appendChild(iconSpan);
            content.appendChild(textSpan);

            btn.appendChild(progressFill);
            btn.appendChild(content);

            btn.resetState = () => {
                delete btn.dataset.activeDownloadId;
                btn.classList.remove('is-starting', 'is-downloading', 'is-completed', 'is-error');
                progressFill.style.width = '0%';
                iconSpan.innerHTML = defaultIconSvg;
                textSpan.textContent = defaultText;
            };

            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();

                if (btn.classList.contains('is-starting') || btn.classList.contains('is-downloading')) {
                    return;
                }

                const downloadId = 'dl_yt_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5);
                btn.dataset.activeDownloadId = downloadId;

                // State 1: Starting/Preparing feedback (Animated downward bouncing arrow + text)
                btn.classList.add('is-starting');
                btn.classList.remove('is-downloading', 'is-completed', 'is-error');
                iconSpan.innerHTML = animArrowIcon;
                textSpan.textContent = i18n.preparing;
                progressFill.style.width = '0%';

                executeDownload(format, false, btn, downloadId);
            });

            return btn;
        }

        // 1. Video button (MP4)
        const videoBtn = createYtNativeButton(videoIcon, i18n.btnMp4Text, 'mp4');

        // 2. Audio button (MP3)
        const audioBtn = createYtNativeButton(audioIcon, i18n.btnMp3Text, 'mp3');

        btnContainer.appendChild(videoBtn);
        btnContainer.appendChild(audioBtn);

        actionBar.prepend(btnContainer);
    }
}

function ensureYtStyles() {
    if (document.getElementById('nf-yt-custom-styles')) return;
    const style = document.createElement('style');
    style.id = 'nf-yt-custom-styles';
    style.textContent = `
        @keyframes nf-arrow-down {
            0% { transform: translateY(-3px); opacity: 0.75; }
            50% { transform: translateY(3px); opacity: 1; }
            100% { transform: translateY(-3px); opacity: 0.75; }
        }
        @keyframes nf-pulse-subtle {
            0%, 100% { opacity: 0.95; }
            50% { opacity: 0.65; }
        }
        .nf-anim-arrow {
            animation: nf-arrow-down 0.85s infinite ease-in-out !important;
            display: inline-block !important;
        }
        .nf-yt-btn {
            position: relative !important;
            display: inline-flex !important;
            align-items: center !important;
            justify-content: center !important;
            gap: 7px !important;
            height: 38px !important;
            min-height: 38px !important;
            max-height: 38px !important;
            border-radius: 19px !important;
            border: none !important;
            padding: 0 18px !important;
            font-family: "YouTube Sans", "Roboto", system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Arial, sans-serif !important;
            font-size: 14.5px !important;
            font-weight: 500 !important;
            line-height: 38px !important;
            letter-spacing: 0.1px !important;
            cursor: pointer !important;
            box-sizing: border-box !important;
            vertical-align: middle !important;
            white-space: nowrap !important;
            user-select: none !important;
            overflow: hidden !important;
            outline: none !important;
            text-decoration: none !important;
            margin: 0 !important;
            flex-shrink: 0 !important;
            backdrop-filter: blur(8px) !important;
            -webkit-backdrop-filter: blur(8px) !important;
            transition: background-color 0.2s cubic-bezier(0.05, 0, 0, 1), transform 0.1s ease !important;
            
            /* Native YouTube variables & fallback */
            background-color: var(--yt-spec-badge-chip-background, rgba(255, 255, 255, 0.1)) !important;
            color: var(--yt-spec-text-primary, #f1f1f1) !important;
            fill: currentColor !important;
        }

        /* Direction handling for leading icon in RTL/LTR */
        html[dir="rtl"] .nf-yt-btn, [dir="rtl"] .nf-yt-btn {
            direction: rtl !important;
        }
        html:not([dir="rtl"]) .nf-yt-btn {
            direction: ltr !important;
        }

        /* YouTube Light Theme */
        html:not([dark]) .nf-yt-btn,
        body:not([dark]) .nf-yt-btn,
        [light] .nf-yt-btn {
            background-color: var(--yt-spec-badge-chip-background, rgba(0, 0, 0, 0.05)) !important;
            color: var(--yt-spec-text-primary, #0f0f0f) !important;
            border: 1px solid rgba(0, 0, 0, 0.04) !important;
        }
        html:not([dark]) .nf-yt-btn:hover:not(:disabled),
        body:not([dark]) .nf-yt-btn:hover:not(:disabled),
        [light] .nf-yt-btn:hover:not(:disabled) {
            background-color: var(--yt-spec-button-chip-background-hover, rgba(0, 0, 0, 0.1)) !important;
        }
        html:not([dark]) .nf-yt-btn:active:not(:disabled),
        body:not([dark]) .nf-yt-btn:active:not(:disabled),
        [light] .nf-yt-btn:active:not(:disabled) {
            background-color: rgba(0, 0, 0, 0.15) !important;
            transform: scale(0.97) !important;
        }

        /* YouTube Dark Theme */
        html[dark] .nf-yt-btn,
        body[dark] .nf-yt-btn,
        ytd-app[dark] .nf-yt-btn,
        [dark] .nf-yt-btn {
            background-color: var(--yt-spec-badge-chip-background, rgba(255, 255, 255, 0.1)) !important;
            color: var(--yt-spec-text-primary, #f1f1f1) !important;
            border: 1px solid rgba(255, 255, 255, 0.06) !important;
        }
        html[dark] .nf-yt-btn:hover:not(:disabled),
        body[dark] .nf-yt-btn:hover:not(:disabled),
        ytd-app[dark] .nf-yt-btn:hover:not(:disabled),
        [dark] .nf-yt-btn:hover:not(:disabled) {
            background-color: var(--yt-spec-button-chip-background-hover, rgba(255, 255, 255, 0.2)) !important;
        }
        html[dark] .nf-yt-btn:active:not(:disabled),
        body[dark] .nf-yt-btn:active:not(:disabled),
        ytd-app[dark] .nf-yt-btn:active:not(:disabled),
        [dark] .nf-yt-btn:active:not(:disabled) {
            background-color: rgba(255, 255, 255, 0.25) !important;
            transform: scale(0.97) !important;
        }

        .nf-yt-btn .nf-btn-icon {
            display: inline-flex !important;
            align-items: center !important;
            justify-content: center !important;
            width: 24px !important;
            height: 24px !important;
            flex-shrink: 0 !important;
        }

        .nf-yt-btn .nf-progress-fill {
            position: absolute !important;
            top: 0 !important;
            bottom: 0 !important;
            right: 0 !important;
            width: 0%;
            height: 100% !important;
            background: linear-gradient(270deg, rgba(62, 166, 255, 0.35), rgba(62, 166, 255, 0.18)) !important;
            border-radius: 18px !important;
            pointer-events: none !important;
            transition: width 0.25s ease-out !important;
            z-index: 1 !important;
        }
        [dir="ltr"] .nf-yt-btn .nf-progress-fill {
            right: auto !important;
            left: 0 !important;
            background: linear-gradient(90deg, rgba(62, 166, 255, 0.35), rgba(62, 166, 255, 0.18)) !important;
        }

        .nf-yt-btn .nf-btn-content {
            position: relative !important;
            z-index: 2 !important;
            display: inline-flex !important;
            align-items: center !important;
            justify-content: center !important;
            gap: 6px !important;
            pointer-events: none !important;
            white-space: nowrap !important;
        }

        .nf-yt-btn.is-starting .nf-btn-content {
            animation: nf-pulse-subtle 1.2s infinite ease-in-out !important;
        }

        .nf-yt-btn.is-completed {
            background-color: rgba(43, 166, 64, 0.15) !important;
            color: #2ba640 !important;
            border: 1px solid rgba(43, 166, 64, 0.3) !important;
        }
        .nf-yt-btn.is-completed .nf-progress-fill {
            display: none !important;
        }

        .nf-yt-btn.is-error {
            background-color: rgba(231, 76, 60, 0.15) !important;
            color: #e74c3c !important;
            border: 1px solid rgba(231, 76, 60, 0.3) !important;
        }
    `;
    (document.head || document.documentElement).appendChild(style);
}

function handleDownloadWsEvent(data) {
    if (!data) return;
    const { type, downloadId, percent } = data;

    const buttons = document.querySelectorAll('.nf-yt-btn');
    buttons.forEach((btn) => {
        if (!btn.dataset.activeDownloadId) return;
        if (downloadId && btn.dataset.activeDownloadId !== downloadId) return;

        const progressFill = btn.querySelector('.nf-progress-fill');
        const iconSpan = btn.querySelector('.nf-btn-icon');
        const textSpan = btn.querySelector('.nf-btn-text');

        if (type === 'progress') {
            btn.classList.remove('is-starting');
            btn.classList.add('is-downloading');
            const pct = Math.min(100, Math.max(0, Math.round(parseFloat(percent) || 0)));
            if (progressFill) progressFill.style.width = `${pct}%`;
            if (iconSpan && !iconSpan.querySelector('.nf-anim-arrow')) {
                iconSpan.innerHTML = `<svg class="nf-anim-arrow" viewBox="0 0 24 24" width="20" height="20" fill="currentColor" focusable="false" style="pointer-events: none; display: block; width: 20px; height: 20px;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`;
            }
            if (textSpan) textSpan.textContent = `${pct}%`;
        } else if (type === 'completed') {
            btn.classList.remove('is-starting', 'is-downloading');
            btn.classList.add('is-completed');
            if (progressFill) progressFill.style.width = '100%';
            if (iconSpan) iconSpan.innerHTML = `<svg viewBox="0 0 24 24" width="20" height="20" fill="#2ba640" focusable="false" style="pointer-events: none; display: block; width: 20px; height: 20px;"><path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/></svg>`;
            if (textSpan) textSpan.textContent = i18n.completed;

            setTimeout(() => {
                if (btn.resetState) btn.resetState();
            }, 3500);
        } else if (type === 'error') {
            btn.classList.remove('is-starting', 'is-downloading');
            btn.classList.add('is-error');
            if (iconSpan) iconSpan.innerHTML = `<svg viewBox="0 0 24 24" width="20" height="20" fill="#e74c3c" focusable="false" style="pointer-events: none; display: block; width: 20px; height: 20px;"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>`;
            if (textSpan) textSpan.textContent = i18n.error;

            setTimeout(() => {
                if (btn.resetState) btn.resetState();
            }, 3500);
        }
    });
}

function injectIframeButton(iframe) {
    const wrapper = iframe.parentElement;
    if (!wrapper) return;

    const container = document.createElement('div');
    container.className = 'nf-iframe-btn-container';
    container.style.cssText = `position: absolute; top: 8px; right: 8px; z-index: 999999; direction: rtl; pointer-events: auto;`;

    const parentStyle = window.getComputedStyle(wrapper);
    if (parentStyle.position === 'static') wrapper.style.position = 'relative';

    const btn = document.createElement('button');
    const dlIconSmall = `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="flex-shrink: 0;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`;
    btn.innerHTML = `${dlIconSmall}<span style="font-size: 12px; font-weight: 600; line-height: 1; color: #ecf0f1;">${i18n.btnClipText}</span>`;

    btn.style.cssText = `background: rgba(44, 62, 80, 0.95); color: #ecf0f1; border: 1px solid #34495e; border-radius: 6px; padding: 4px 10px; font-family: 'Varela Round', system-ui, sans-serif; display: flex; align-items: center; gap: 5px; cursor: pointer; opacity: 0.85; box-shadow: 0 2px 5px rgba(0,0,0,0.5); transition: opacity 0.2s, background 0.2s, transform 0.2s; outline: none;`;

    btn.onmouseenter = () => { btn.style.background = 'rgba(52, 73, 94, 1)'; btn.style.transform = 'scale(1.03)'; btn.style.opacity = '1'; };
    btn.onmouseleave = () => { btn.style.background = 'rgba(44, 62, 80, 0.95)'; btn.style.transform = 'scale(1)'; btn.style.opacity = '0.85'; };

    btn.onclick = (e) => {
        e.preventDefault(); e.stopPropagation();
        let url = iframe.src || iframe.getAttribute('data-src');
        if (url && url.includes('/embed/')) {
            url = `https://www.youtube.com/watch?v=${url.split('/embed/')[1].split('?')[0]}`;
        }
        chrome.runtime.sendMessage({
            action: 'open_app',
            url: url,
            directUrl: null,
            title: null
        });
    };

    wrapper.appendChild(container);
    container.appendChild(btn);
}

function injectSingleGenericButton(videoEl) {
    const actionBar = videoEl.parentElement;
    const btn = document.createElement('button');
    btn.className = 'nf-generic-btn';
    btn.videoRef = videoEl;

    const dlIconSmall = `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="flex-shrink: 0;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`;
    btn.innerHTML = `${dlIconSmall}<span class="nf-btn-text" style="font-size: 12px; font-weight: 600; line-height: 1;">${i18n.btnVideoText}</span>`;
    btn.style.cssText = `position: absolute; top: 8px; right: 8px; z-index: 9999; background: rgba(44, 62, 80, 0.95); color: #ecf0f1; border: 1px solid #34495e; border-radius: 6px; padding: 4px 8px; font-family: 'Varela Round', system-ui, sans-serif; display: flex; align-items: center; gap: 5px; direction: rtl; cursor: pointer; opacity: 0; box-shadow: 0 2px 6px rgba(0,0,0,0.3); transition: opacity 0.2s, background 0.2s, transform 0.2s;`;

    btn.onmouseenter = () => { btn.style.background = 'rgba(52, 73, 94, 1)'; btn.style.transform = 'scale(1.03)'; };
    btn.onmouseleave = () => { btn.style.background = 'rgba(44, 62, 80, 0.95)'; btn.style.transform = 'scale(1)'; };

    const parentStyle = window.getComputedStyle(actionBar);
    if (parentStyle.position === 'static') actionBar.style.position = 'relative';

    actionBar.addEventListener('mouseenter', () => btn.style.opacity = '1');
    actionBar.addEventListener('mouseleave', () => btn.style.opacity = '0');

    btn.onclick = (e) => {
        e.preventDefault(); e.stopPropagation();

        const data = getTargetUrlAndMetadata(false, btn);
        const isYt = window.location.hostname.includes('youtube.com') || window.location.hostname.includes('youtube-nocookie.com');
        if (!isYt) {
            const specificSniffedUrl = videoEl.dataset.sniffedUrl;
            if (specificSniffedUrl) {
                data.directUrl = specificSniffedUrl;
            }
        }

        chrome.runtime.sendMessage({
            action: 'open_app',
            url: data.url,
            directUrl: data.directUrl,
            title: data.title,
            thumbnail: data.thumbnail
        });
    };

    actionBar.appendChild(btn);
    const resizeObserver = new ResizeObserver(entries => {
        for (let entry of entries) {
            const textSpan = btn.querySelector('.nf-btn-text');
            if (textSpan) {
                textSpan.style.display = entry.contentRect.width < 280 ? 'none' : 'inline';
            }
        }
    });
    resizeObserver.observe(actionBar);
}

document.addEventListener('yt-navigate-finish', () => { setTimeout(tryInjectButton, 200); setTimeout(tryInjectButton, 800); setTimeout(tryInjectButton, 1500); });
document.addEventListener('DOMContentLoaded', tryInjectButton);
const observer = new MutationObserver(() => { tryInjectButton(); });
if (document.body) { observer.observe(document.body, { childList: true, subtree: true }); }
setInterval(tryInjectButton, 1000);
