function sanitizeFilename(name) {
    if (!name) return 'download';
    return name.replace(/[<>:"/\\|?*\x00-\x1F]/g, '_').trim().substring(0, 150);
}

function wakeServer() {
    return new Promise((resolve) => {
        // 1. Try Native Messaging
        try {
            chrome.runtime.sendNativeMessage('com.nfdownloader.host', { action: 'start_server' }, (resp) => {
                if (!chrome.runtime.lastError && resp && resp.status === 'ok') {
                    resolve(true);
                } else {
                    // 2. Fallback: Trigger via Custom Protocol URL
                    try {
                        chrome.tabs.create({ url: 'ssshmuldownloader://launch', active: false }, (tab) => {
                            if (tab && tab.id) {
                                setTimeout(() => { try { chrome.tabs.remove(tab.id); } catch (e) { } }, 1200);
                            }
                        });
                    } catch (e) { }
                    resolve(true);
                }
            });
        } catch (e) {
            try {
                chrome.tabs.create({ url: 'ssshmuldownloader://launch', active: false }, (tab) => {
                    if (tab && tab.id) {
                        setTimeout(() => { try { chrome.tabs.remove(tab.id); } catch (e) { } }, 1200);
                    }
                });
            } catch (err) { }
            resolve(true);
        }
    });
}

function cleanSongQuery(title) {
    if (!title) return '';
    let cleaned = title;
    // Remove " - YouTube" or similar site suffixes
    cleaned = cleaned.replace(/\s*-\s*YouTube$/i, '');
    // Remove common video-only tags, parentheses, and brackets
    cleaned = cleaned.replace(/[([{\-]\s*(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי|מילים|אודיו|גרסת\s*אולפן)\s*[)\]}]/gi, '');
    cleaned = cleaned.replace(/(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי)\b/gi, '');
    // Clean redundant extra spaces and trailing dashes
    cleaned = cleaned.replace(/[\s\-_|:]+$/g, '').replace(/^[\s\-_|:]+/g, '').replace(/\s{2,}/g, ' ').trim();
    return cleaned;
}

async function findCleanAudioVersion(url, currentTitle, originalChannel = '') {
    try {
        if (!url || (!url.includes('youtube.com') && !url.includes('youtu.be'))) {
            return null;
        }

        const query = cleanSongQuery(currentTitle);
        if (!query || query.length < 3) return null;

        // If original channel is unknown, try to fetch source video page once to detect its channel/artist
        let sourceChannel = originalChannel ? originalChannel.trim() : '';
        if (!sourceChannel && (url.includes('youtube.com') || url.includes('youtu.be'))) {
            try {
                const vidPageResp = await fetch(url, {
                    headers: { 'Accept-Language': 'he,en;q=0.9', 'User-Agent': navigator.userAgent }
                });
                if (vidPageResp.ok) {
                    const vidHtml = await vidPageResp.text();
                    const chMatch = vidHtml.match(/"ownerChannelName":"([^"]+)"/) ||
                                    vidHtml.match(/"author":"([^"]+)"/) ||
                                    vidHtml.match(/"channelName":"([^"]+)"/);
                    if (chMatch && chMatch[1]) {
                        sourceChannel = chMatch[1];
                    }
                }
            } catch (e) { }
        }

        const normSource = sourceChannel ? sourceChannel.toLowerCase().replace(/official|channel|vevo|records|music|ערוץ|רשמי|הערוץ הרשמי|topic|נושא/gi, '').replace(/[\s\-_]+/g, ' ').trim() : '';

        // Search YouTube for official Topic / Audio version
        const searchQuery = `${query} Audio`;
        const searchUrl = `https://www.youtube.com/results?search_query=${encodeURIComponent(searchQuery)}&sp=EgIQAQ%253D%253D`;
        const resp = await fetch(searchUrl, {
            headers: {
                'Accept-Language': 'he,en;q=0.9',
                'User-Agent': navigator.userAgent
            }
        });
        if (!resp.ok) return null;
        const html = await resp.text();

        let jsonStr = '';
        const match = html.match(/var ytInitialData = ({.*?});<\/script>/s) || html.match(/ytInitialData\s*=\s*({.*?});/s);
        if (match && match[1]) jsonStr = match[1];
        if (!jsonStr) return null;

        const data = JSON.parse(jsonStr);
        const contents = data?.contents?.twoColumnSearchResultsRenderer?.primaryContents?.sectionListRenderer?.contents;
        if (!Array.isArray(contents)) return null;

        for (const section of contents) {
            const itemSection = section?.itemSectionRenderer?.contents;
            if (Array.isArray(itemSection)) {
                for (const item of itemSection) {
                    const videoRenderer = item.videoRenderer;
                    if (videoRenderer && videoRenderer.videoId) {
                        const videoId = videoRenderer.videoId;
                        const title = videoRenderer.title?.runs?.[0]?.text || videoRenderer.title?.simpleText || '';
                        const channel = videoRenderer.ownerText?.runs?.[0]?.text || videoRenderer.shortBylineText?.runs?.[0]?.text || '';
                        const isTopic = channel.includes('Topic') || channel.includes('נושא') || title.toLowerCase().includes('audio') || title.includes('אודיו');
                        
                        const normCandidateChannel = channel.toLowerCase().replace(/official|channel|vevo|records|music|ערוץ|רשמי|הערוץ הרשמי|topic|נושא/gi, '').replace(/[\s\-_]+/g, ' ').trim();

                        // Must match original channel / artist topic if source channel is known
                        let isSameArtistChannel = true;
                        if (normSource && normCandidateChannel) {
                            const exactMatch = normCandidateChannel === normSource;
                            const candidateIncludesSource = normCandidateChannel.includes(normSource) && normSource.length > 2;
                            const sourceIncludesCandidate = normSource.includes(normCandidateChannel) && normCandidateChannel.length > 2;
                            isSameArtistChannel = exactMatch || candidateIncludesSource || sourceIncludesCandidate;
                        }

                        // If it's a Topic / Official Audio release from the same artist/channel, prefer it
                        if (isSameArtistChannel && (isTopic || channel.toLowerCase().includes('records') || channel.toLowerCase().includes('music') || channel.toLowerCase().includes(normSource))) {
                            const thumb = videoRenderer.thumbnail?.thumbnails?.slice(-1)[0]?.url || `https://i.ytimg.com/vi/${videoId}/hqdefault.jpg`;
                            return {
                                url: `https://www.youtube.com/watch?v=${videoId}`,
                                title: title || currentTitle,
                                thumbnail: thumb
                            };
                        }
                    }
                }
            }
        }
    } catch (e) {
        console.error('Error resolving clean audio version:', e);
    }
    return null;
}

async function sendViaWebSocket(req, retryOnFail = true) {
    let finalUrl = req.url;
    let finalTitle = req.title;
    let finalThumbnail = req.thumbnail;

    const storage = await chrome.storage.local.get([
        'savedMp3Quality', 
        'savedMp4Quality', 
        'customSavePath',
        'downloadSubsDefault',
        'subsLangDefault',
        'subsTypeDefault',
        'preferCleanAudio'
    ]);

    const isVideo = req.format === 'mp4';
    const cleanAudioEnabled = storage.preferCleanAudio !== false; // Default true

    // Run clean audio resolution and cookie retrieval in parallel for max speed
    const isYouTube = finalUrl && (finalUrl.includes('youtube.com') || finalUrl.includes('youtu.be'));
    const cleanPromise = (!isVideo && cleanAudioEnabled && isYouTube)
        ? findCleanAudioVersion(finalUrl, finalTitle, req.channel || '')
        : Promise.resolve(null);
    const cookiesPromise = isYouTube
        ? getYoutubeCookies().catch(() => null)
        : Promise.resolve(null);

    const [cleanVer, cookies] = await Promise.all([cleanPromise, cookiesPromise]);

    if (cleanVer && cleanVer.url) {
        finalUrl = cleanVer.url;
        if (cleanVer.title) finalTitle = cleanVer.title;
        if (cleanVer.thumbnail) finalThumbnail = cleanVer.thumbnail;
    }

    const chosenFormat = isVideo 
        ? (storage.savedMp4Quality || 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best') 
        : (storage.savedMp3Quality || 'mp3_high');

    const downloadId = req.downloadId || (Date.now().toString(36) + Math.random().toString(36).substr(2));
    const msg = {
        type: isVideo ? 'download_video_advanced' : 'download_advanced',
        downloadId: downloadId,
        url: finalUrl,
        directUrl: req.directUrl || null,
        customTitle: finalTitle || null,
        customThumbnail: finalThumbnail || null,
        customSavePath: storage.customSavePath || null,
        formatId: chosenFormat,
        playlist: false,
        qualityText: isVideo ? 'איכות מיטבית (MP4)' : 'MP3 - גרסת אולפן / נקייה',
        cookies: cookies || null,
        downloadSubs: false, // Subtitles extracted directly in browser
        subsLang: storage.subsLangDefault || 'he',
        subsType: storage.subsTypeDefault || 'separate'
    };

    const trySend = () => {
        return new Promise((resolve) => {
            initPersistentWs();
            if (persistentWs && persistentWs.readyState === WebSocket.OPEN) {
                try {
                    persistentWs.send(JSON.stringify(msg));
                    resolve(true);
                    return;
                } catch (e) { }
            }

            try {
                const ws = new WebSocket('ws://localhost:9595/ws');
                const timer = setTimeout(() => {
                    try { ws.close(); } catch (e) { }
                    resolve(false);
                }, 1500);

                ws.onopen = () => {
                    clearTimeout(timer);
                    ws.send(JSON.stringify(msg));
                    setTimeout(() => {
                        try { ws.close(); } catch (e) { }
                        resolve(true);
                    }, 400);
                };

                ws.onerror = () => {
                    clearTimeout(timer);
                    resolve(false);
                };
            } catch (err) {
                resolve(false);
            }
        });
    };

    let sent = await trySend();
    if (!sent && retryOnFail) {
        // Automatically start the server!
        await wakeServer();
        // Wait and retry up to 6 times (3 seconds total)
        for (let i = 0; i < 6; i++) {
            await new Promise(r => setTimeout(r, 500));
            sent = await trySend();
            if (sent) break;
        }
    }
    return { success: !!sent, downloadId };
}

function checkFileExists(url) {
    if (!url) return Promise.resolve(false);
    return fetch(url, { method: 'GET' })
        .then(res => res.ok)
        .catch(() => false);
}

function getYoutubeCookies() {
    return new Promise((resolve) => {
        chrome.cookies.getAll({}, (allCookies) => {
            if (!allCookies || allCookies.length === 0) {
                resolve(null);
                return;
            }
            const ytCookies = allCookies.filter(c => {
                if (!c.domain) return false;
                const d = c.domain.toLowerCase();
                if (d.includes('docs.google') || d.includes('cloud.google') || d.includes('drive.google') || 
                    d.includes('mail.google') || d.includes('gemini.google') || d.includes('play.google')) {
                    return false;
                }
                return d.includes('youtube.com') || d.includes('googlevideo.com') || d.includes('youtu.be') || d === '.google.com' || d === 'google.com';
            });
            if (ytCookies.length === 0) {
                resolve(null);
                return;
            }
            resolve(formatCookiesNetscape(ytCookies));
        });
    });
}

function formatCookiesNetscape(cookies) {
    let fileContent = "# Netscape HTTP Cookie File\n";
    cookies.forEach(c => {
        let domain = c.domain;
        let incSub = domain.startsWith('.') ? "TRUE" : "FALSE";
        let secure = c.secure ? "TRUE" : "FALSE";
        let exp = c.expirationDate ? Math.round(c.expirationDate) : 0;
        fileContent += `${domain}\t${incSub}\t${c.path}\t${secure}\t${exp}\t${c.name}\t${c.value}\n`;
    });
    return fileContent;
}

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === 'request_ext_settings') {
        chrome.storage.local.get(['userConfig'], (result) => {
            sendResponse({ config: result.userConfig || { btnStyle: 'classic' } });
        });
        return true;
    }
    if (request.action === 'get_cookies') {
        getYoutubeCookies().then(cookies => sendResponse({ cookies }));
        return true;
    }
    if (request.action === 'get_latest_media') {
        sendResponse({ mediaUrl: tabMediaUrls[sender.tab.id] || null });
        return true;
    }
    if (request.action === 'set_ext_config') {
        chrome.storage.local.get(['userConfig'], (res) => {
            let uConf = res.userConfig || {}; uConf[request.key] = request.value;
            chrome.storage.local.set({ userConfig: uConf });
        });
    }

    // Direct background download (MP3/MP4) without switching tabs
    if (request.action === 'direct_download') {
        sendViaWebSocket(request).then((res) => {
            if (res && res.success) {
                sendResponse({ success: true, method: 'websocket', downloadId: res.downloadId });
            } else {
                sendResponse({ success: false, reason: 'app_not_running' });
            }
        });
        return true;
    }

    if (request.action === 'save_subtitles') {
        const safeTitle = (request.title || 'subtitles').replace(/[\\/:*?"<>|]/g, '_').trim();
        const dataUrl = 'data:text/plain;charset=utf-8,' + encodeURIComponent(request.content || '');
        chrome.downloads.download({
            url: dataUrl,
            filename: `${safeTitle}.srt`,
            saveAs: false
        }, (downloadId) => {
            sendResponse({ success: !!downloadId });
        });
        return true;
    }

    if (request.action === 'open_app') {
        wakeServer();
        sendResponse({ success: true });
        return true;
    }
});

let tabMediaUrls = {};
const validExtensions = ['.m3u8', '.mpd', '.mp4'];
const chunkExtensions = ['.m4s', '.ts', 'init.mp4', 'seg.mp4'];
const adRegex = /(?:\/|_|-)(ads?|video_ads)(?:\/|_|-|\.)/i;
const adKeywords = ['doubleclick.net', 'googlesyndication.com', 'ima3', 'vpaid'];

chrome.webRequest.onBeforeRequest.addListener(
    (details) => {
        const url = details.url.toLowerCase();
        const isTarget = validExtensions.some(ext => url.includes(ext));

        if (isTarget) {
            const isChunk = chunkExtensions.some(ext => url.includes(ext));
            const isAd = adRegex.test(url) || adKeywords.some(kw => url.includes(kw));

            if (!isChunk && !isAd) {
                chrome.tabs.sendMessage(details.tabId, {
                    action: 'bind_new_media',
                    url: details.url
                }).catch(() => { });
            }
        }
    },
    { urls: ["<all_urls>"] }
);
chrome.tabs.onRemoved.addListener((tabId) => {
    delete tabMediaUrls[tabId];
});

// Persistent WebSocket connection to app server for real-time progress broadcast
let persistentWs = null;
let reconnectTimer = null;

function broadcastWsEvent(data) {
    try {
        chrome.tabs.query({}, (tabs) => {
            if (!tabs || tabs.length === 0) return;
            tabs.forEach((tab) => {
                if (tab && tab.id) {
                    chrome.tabs.sendMessage(tab.id, {
                        action: 'download_ws_event',
                        data: data
                    }).catch(() => { });
                }
            });
        });
    } catch (e) { }
}

function initPersistentWs() {
    if (persistentWs && (persistentWs.readyState === WebSocket.OPEN || persistentWs.readyState === WebSocket.CONNECTING)) {
        return;
    }
    try {
        persistentWs = new WebSocket('ws://localhost:9595/ws');
        persistentWs.onopen = () => {
            try {
                persistentWs.send(JSON.stringify({ type: 'extension_ping' }));
            } catch (e) { }
        };
        persistentWs.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                broadcastWsEvent(data);
            } catch (e) { }
        };
        persistentWs.onclose = () => {
            persistentWs = null;
            if (!reconnectTimer) {
                reconnectTimer = setTimeout(() => {
                    reconnectTimer = null;
                    initPersistentWs();
                }, 3000);
            }
        };
        persistentWs.onerror = () => {
            try { persistentWs.close(); } catch (e) { }
        };
    } catch (e) {
        if (!reconnectTimer) {
            reconnectTimer = setTimeout(() => {
                reconnectTimer = null;
                initPersistentWs();
            }, 3000);
        }
    }
}

initPersistentWs();
setInterval(initPersistentWs, 15000);

