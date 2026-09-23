
let ws = null;
let currentFormatType = 'mp3';
let selectedMp3Quality = 'mp3_high';
let selectedMp4Quality = 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best';
let activeDownloads = {};
let detectedMedia = { url: '', title: '', thumbnail: '' };
let videoPlaylist = [];
let searchResults = [];
let selectedSearchCount = 10;

const qualityLabels = {
    'mp3_high': 'איכות גבוהה',
    'mp3_medium': 'איכות רגילה',
    'raw_audio': 'שמע מקורי (Raw)',
    'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best': 'מיטבית (Best)',
    'bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080]': '1080p Full HD',
    'bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720]': '720p HD',
    'bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480]': '480p SD'
};

const serverStatusPill = document.getElementById('server-status');
const statusText = document.getElementById('status-text');
const offlineBanner = document.getElementById('server-offline-banner');
const startServerBtn = document.getElementById('start-server-btn');
const settingsBtn = document.getElementById('settings-btn');
const mainView = document.getElementById('main-view');
const settingsView = document.getElementById('settings-view');
const currentQualityBadge = document.getElementById('current-quality-badge');
const currentQualityBadgeText = document.getElementById('current-quality-badge-text');
const qualityModalOverlay = document.getElementById('quality-modal-overlay');
const closeQualityModalBtn = document.getElementById('close-quality-modal-btn');
const modalTabMp3 = document.getElementById('modal-tab-mp3');
const modalTabMp4 = document.getElementById('modal-tab-mp4');
const modalMp3Options = document.getElementById('modal-mp3-options');
const modalMp4Options = document.getElementById('modal-mp4-options');
const savePathInput = document.getElementById('save-path-input');
const browseSavePathBtn = document.getElementById('browse-save-path-btn');
const resetPathBtn = document.getElementById('reset-path-btn');
const stopServerSettingsBtn = document.getElementById('stop-server-settings-btn');

// Mode Switch Elements
const modeLinkBtn = document.getElementById('mode-link');
const modeSearchBtn = document.getElementById('mode-search');
const linkInputSection = document.getElementById('link-input-section');
const artistSearchSection = document.getElementById('artist-search-section');

// Search Elements
const artistQueryInput = document.getElementById('artist-query-input');
const artistClearBtn = document.getElementById('artist-clear-btn');
const artistSearchBtn = document.getElementById('artist-search-btn');
const searchResultsSection = document.getElementById('search-results-section');
const searchResultsList = document.getElementById('search-results-list');
const resultsCountBadge = document.getElementById('results-count-badge');
const selectAllResultsCb = document.getElementById('select-all-results');
const addSelectedBtn = document.getElementById('add-selected-btn');
const countChips = document.querySelectorAll('.count-chip');
const customCountInput = document.getElementById('custom-count-input');

const urlInput = document.getElementById('url-input');
const pasteBtn = document.getElementById('paste-btn');
const clearBtn = document.getElementById('clear-btn');
const addBtn = document.getElementById('add-btn');
const mediaPreview = document.getElementById('media-preview');
const mediaThumb = document.getElementById('media-thumb');
const mediaTitle = document.getElementById('media-title');
const mediaDomain = document.getElementById('media-domain');
const tabMp3 = document.getElementById('tab-mp3');
const tabMp4 = document.getElementById('tab-mp4');
const downloadsSection = document.getElementById('downloads-section');
const activeDownloadsList = document.getElementById('active-downloads-list');
const activeCountBadge = document.getElementById('active-count-badge');
const genericBtnToggle = document.getElementById('generic-btn-toggle');
const playlistSection = document.getElementById('playlist-section');
const playlistList = document.getElementById('playlist-list');
const playlistCountBadge = document.getElementById('playlist-count-badge');
const downloadAllBtn = document.getElementById('download-all-btn');
const removeAllBtn = document.getElementById('remove-all-btn');
const cancelAllDownloadsBtn = document.getElementById('cancel-all-downloads-btn');
const toggleAllPauseBtn = document.getElementById('toggle-all-pause-btn');

// Toast notification
function showToast(msg, duration = 3000) {
    const toast = document.getElementById('toast');
    toast.textContent = msg;
    toast.classList.remove('hidden');
    setTimeout(() => toast.classList.add('hidden'), duration);
}

function updateQualityBadge() {
    const curQuality = currentFormatType === 'mp3' ? selectedMp3Quality : selectedMp4Quality;
    const label = qualityLabels[curQuality] || curQuality;
    
    if (currentQualityBadgeText) {
        currentQualityBadgeText.textContent = label;
    } else if (currentQualityBadge) {
        currentQualityBadge.textContent = label;
    }

    // Sync modal tabs & options
    if (modalTabMp3 && modalTabMp4 && modalMp3Options && modalMp4Options) {
        if (currentFormatType === 'mp3') {
            modalTabMp3.classList.add('active');
            modalTabMp4.classList.remove('active');
            modalMp3Options.classList.remove('hidden');
            modalMp4Options.classList.add('hidden');
        } else {
            modalTabMp4.classList.add('active');
            modalTabMp3.classList.remove('active');
            modalMp4Options.classList.remove('hidden');
            modalMp3Options.classList.add('hidden');
        }

        // Sync modal quality option buttons
        document.querySelectorAll('.quality-option-btn').forEach(btn => {
            const bType = btn.getAttribute('data-type');
            const bFmt = btn.getAttribute('data-format');
            if (bType === 'mp3') {
                btn.classList.toggle('active', bFmt === selectedMp3Quality);
            } else {
                btn.classList.toggle('active', bFmt === selectedMp4Quality);
            }
        });
    }

    // Sync settings view chips
    document.querySelectorAll('#mp3-qualities .chip').forEach(c => {
        c.classList.toggle('active', c.getAttribute('data-format') === selectedMp3Quality);
    });
    document.querySelectorAll('#mp4-qualities .chip').forEach(c => {
        c.classList.toggle('active', c.getAttribute('data-format') === selectedMp4Quality);
    });

    // Sync main format tabs
    if (tabMp3 && tabMp4) {
        tabMp3.classList.toggle('active', currentFormatType === 'mp3');
        tabMp4.classList.toggle('active', currentFormatType === 'mp4');
    }
}

function openQualityModal() {
    if (!qualityModalOverlay) return;
    updateQualityBadge();
    qualityModalOverlay.classList.remove('hidden');
    if (currentQualityBadge) currentQualityBadge.classList.add('open');
}

function closeQualityModal() {
    if (!qualityModalOverlay) return;
    qualityModalOverlay.classList.add('hidden');
    if (currentQualityBadge) currentQualityBadge.classList.remove('open');
}

function selectFormatAndQuality(formatType, qualityFormat, showFeedback = true) {
    currentFormatType = formatType;
    if (formatType === 'mp3') {
        if (qualityFormat) selectedMp3Quality = qualityFormat;
        chrome.storage.local.set({ savedMp3Quality: selectedMp3Quality, savedFormatType: 'mp3' });
    } else {
        if (qualityFormat) selectedMp4Quality = qualityFormat;
        chrome.storage.local.set({ savedMp4Quality: selectedMp4Quality, savedFormatType: 'mp4' });
    }
    updateQualityBadge();
    if (showFeedback) {
        const curQ = formatType === 'mp3' ? selectedMp3Quality : selectedMp4Quality;
        showToast(`✨ הוגדר: ${formatType.toUpperCase()} - ${qualityLabels[curQ] || curQ}`);
    }
}

// Start Server / Launch Desktop Application
let reconnectTimer = null;
let pollInterval = null;

function triggerProtocolLaunch() {
    const schemeUrl = 'ssshmuldownloader://launch';
    try {
        if (chrome && chrome.tabs && chrome.tabs.create) {
            chrome.tabs.create({ url: schemeUrl, active: false }, (tab) => {
                if (tab && tab.id) {
                    setTimeout(() => {
                        try { chrome.tabs.remove(tab.id); } catch (e) { }
                    }, 1200);
                }
            });
        }
    } catch (e) { }

    try {
        const ifr = document.createElement('iframe');
        ifr.style.display = 'none';
        ifr.src = schemeUrl;
        document.body.appendChild(ifr);
        setTimeout(() => ifr.remove(), 1200);
    } catch (e) { }
}

function startServer() {
    showToast('🚀 מפעיל את התוכנה ברקע...');
    try {
        if (chrome && chrome.runtime && chrome.runtime.sendNativeMessage) {
            chrome.runtime.sendNativeMessage('com.nfdownloader.host', { action: 'start_server' }, (resp) => {
                if (chrome.runtime.lastError || !resp || resp.status !== 'ok') {
                    triggerProtocolLaunch();
                }
            });
        } else {
            triggerProtocolLaunch();
        }
    } catch (e) {
        triggerProtocolLaunch();
    }

    // Active quick-polling to connect immediately when the software starts
    if (pollInterval) clearInterval(pollInterval);
    let attempts = 0;
    pollInterval = setInterval(() => {
        attempts++;
        if (ws && ws.readyState === WebSocket.OPEN) {
            clearInterval(pollInterval);
            pollInterval = null;
            return;
        }
        connectWebSocket();
        if (attempts > 25) {
            clearInterval(pollInterval);
            pollInterval = null;
        }
    }, 500);
}

// Stop / Shutdown Server manually
function stopServer() {
    showToast('🛑 שולח בקשת עצירה לשרת...');
    let signaled = false;

    // 1. Send via WebSocket if connected
    if (ws && ws.readyState === WebSocket.OPEN) {
        try {
            ws.send(JSON.stringify({ type: 'shutdown_server' }));
            signaled = true;
        } catch (e) {}
    }

    // 2. Send via HTTP shutdown endpoint
    try {
        fetch('http://localhost:9595/shutdown', { method: 'POST', mode: 'no-cors' }).catch(() => {});
        signaled = true;
    } catch (e) {}

    // 3. Send via Native Messaging Host
    try {
        chrome.runtime.sendNativeMessage('com.nfdownloader.host', { action: 'stop_server' }, () => {
            if (chrome.runtime.lastError) {}
        });
    } catch (e) {}

    setTimeout(() => {
        if (ws) {
            try { ws.close(); } catch (e) {}
        }
        serverStatusPill.className = 'status-pill offline';
        statusText.textContent = 'שרת מנותק (לחץ להפעלה)';
        showToast('✔️ השרת נעצר בהצלחה');
    }, 1000);
}

// Get YouTube Cookies
function getYoutubeCookies() {
    return new Promise((resolve) => {
        chrome.cookies.getAll({}, (allCookies) => {
            if (!allCookies || allCookies.length === 0) {
                resolve(null);
                return;
            }
            const ytCookies = allCookies.filter(c => 
                c.domain.includes('youtube.com') || 
                c.domain.includes('google.com') || 
                c.domain.includes('youtu.be')
            );
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

// WebSocket Connection
function connectWebSocket() {
    try {
        ws = new WebSocket('ws://localhost:9595/ws');

        ws.onopen = () => {
            serverStatusPill.className = 'status-pill online';
            statusText.textContent = 'שרת מחובר';
            offlineBanner.classList.add('hidden');
        };

        ws.onclose = () => {
            serverStatusPill.className = 'status-pill offline';
            statusText.textContent = 'שרת מנותק (לחץ להפעלה)';
            if (settingsView.classList.contains('hidden')) {
                offlineBanner.classList.remove('hidden');
            }
            setTimeout(connectWebSocket, 1500);
        };

        ws.onerror = () => {
            serverStatusPill.className = 'status-pill offline';
            statusText.textContent = 'שרת מנותק (לחץ להפעלה)';
            if (settingsView.classList.contains('hidden')) {
                offlineBanner.classList.remove('hidden');
            }
        };

        ws.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                handleServerMessage(data);
            } catch (e) { }
        };
    } catch (e) {
        setTimeout(connectWebSocket, 1500);
    }
}

// Handle Server Messages
function handleServerMessage(data) {
    const { type, percent, speed, downloadId, title, thumbnail, error, activeDownloads: serverActiveDownloads } = data;

    // Handle Folder Selection Events
    if (type === 'destination_selected' && data.path) {
        const pathInput = document.getElementById('save-path-input');
        if (pathInput) pathInput.value = data.path;
        chrome.storage.local.set({ customSavePath: data.path });
        showToast(`📁 תיקיית שמירה נבחרה: ${data.path}`);
        return;
    }

    if ((type === 'restoreState' || type === 'restore_state') && Array.isArray(serverActiveDownloads)) {
        serverActiveDownloads.forEach(task => {
            if (task && (task.downloadId || task.id)) {
                const taskId = task.downloadId || task.id;
                activeDownloads[taskId] = {
                    id: taskId,
                    title: task.title || 'מוריד מדיה...',
                    thumbnail: task.thumbnail || 'icon.png',
                    percent: task.lastPercent || '0',
                    speed: task.lastSpeed || '',
                    isPaused: task.isPaused === true || task.currentState === 'paused'
                };
                renderDownloadItem(activeDownloads[taskId]);
            }
        });
        updateDownloadsVisibility();
        saveActiveDownloadsToStorage();
        return;
    }

    if (type === 'paused' && downloadId) {
        if (activeDownloads[downloadId]) {
            activeDownloads[downloadId].isPaused = true;
            saveActiveDownloadsToStorage();
        }
        const row = document.getElementById(`item-${downloadId}`);
        if (row) {
            const statusDesc = row.querySelector('.item-status-desc');
            const pauseBtn = row.querySelector('.item-pause-btn');
            const fill = row.querySelector('.progress-fill');
            if (statusDesc) {
                statusDesc.textContent = 'מושהה ⏸';
                statusDesc.style.color = '#f59e0b';
            }
            if (fill) fill.classList.add('paused');
            if (pauseBtn) {
                pauseBtn.classList.add('is-paused');
                pauseBtn.title = 'המשך הורדה';
                pauseBtn.innerHTML = '<span>המשך</span><span>▶</span>';
            }
        }
        updateToggleAllPauseBtnState();
        return;
    }

    if (type === 'resumed' && downloadId) {
        if (activeDownloads[downloadId]) {
            activeDownloads[downloadId].isPaused = false;
            saveActiveDownloadsToStorage();
        }
        const row = document.getElementById(`item-${downloadId}`);
        if (row) {
            const statusDesc = row.querySelector('.item-status-desc');
            const pauseBtn = row.querySelector('.item-pause-btn');
            const fill = row.querySelector('.progress-fill');
            if (statusDesc) {
                statusDesc.textContent = 'מתחבר מחדש...';
                statusDesc.style.color = '';
            }
            if (fill) fill.classList.remove('paused');
            if (pauseBtn) {
                pauseBtn.classList.remove('is-paused');
                pauseBtn.title = 'השהה הורדה';
                pauseBtn.innerHTML = '<span>השהה</span><span>⏸</span>';
            }
        }
        updateToggleAllPauseBtnState();
        return;
    }

    if (!downloadId) return;

    if (!activeDownloads[downloadId]) {
        activeDownloads[downloadId] = {
            id: downloadId,
            title: title || 'מוריד מדיה...',
            thumbnail: thumbnail || 'icon.png',
            percent: percent || '0',
            speed: speed || '',
            isPaused: false
        };
        renderDownloadItem(activeDownloads[downloadId]);
        saveActiveDownloadsToStorage();
    }

    const item = activeDownloads[downloadId];
    const row = document.getElementById(`item-${downloadId}`);
    if (!row) return;

    const fill = row.querySelector('.progress-fill');
    const percentEl = row.querySelector('.item-percent-badge');
    const speedEl = row.querySelector('.item-speed');
    const statusDesc = row.querySelector('.item-status-desc');
    const titleEl = row.querySelector('.item-title');
    const thumbEl = row.querySelector('.item-thumb');
    const pauseBtn = row.querySelector('.item-pause-btn');
    const cancelBtn = row.querySelector('.item-cancel-btn');

    if (title && titleEl) {
        titleEl.textContent = title;
        item.title = title;
    }
    if (thumbnail && thumbEl) {
        thumbEl.src = thumbnail;
        item.thumbnail = thumbnail;
    }

    if (type === 'progress') {
        item.isPaused = false;
        const p = parseFloat(percent) || 0;
        fill.style.width = p + '%';
        fill.className = 'progress-fill';
        percentEl.textContent = p.toFixed(1) + '%';
        statusDesc.textContent = 'מוריד...';
        statusDesc.style.color = '';
        speedEl.textContent = speed ? `(${speed})` : '';
        if (pauseBtn) {
            pauseBtn.classList.remove('is-paused');
            pauseBtn.title = 'השהה הורדה';
            pauseBtn.innerHTML = '<span>השהה</span><span>⏸</span>';
        }
    } else if (type === 'processing' || type === 'merging') {
        item.isPaused = false;
        fill.className = 'progress-fill processing';
        percentEl.textContent = '100%';
        statusDesc.textContent = 'מעבד וממיר...';
        statusDesc.style.color = '';
        speedEl.textContent = '';
        if (pauseBtn) {
            pauseBtn.classList.remove('is-paused');
            pauseBtn.title = 'השהה הורדה';
            pauseBtn.innerHTML = '<span>השהה</span><span>⏸</span>';
        }
    } else if (type === 'success') {
        fill.style.width = '100%';
        fill.className = 'progress-fill success';
        percentEl.textContent = '100%';
        statusDesc.textContent = 'הושלם בהצלחה! ✓';
        statusDesc.style.color = '#10b981';
        speedEl.textContent = '';
        if (pauseBtn) pauseBtn.remove();
        if (cancelBtn) cancelBtn.remove();
        setTimeout(() => {
            delete activeDownloads[downloadId];
            row.remove();
            updateDownloadsVisibility();
            saveActiveDownloadsToStorage();
        }, 6000); // Keep success message longer
    } else if (type === 'error') {
        fill.style.width = '100%';
        fill.style.background = '#ef4444';
        percentEl.textContent = 'שגיאה';
        
        let friendlyErr = error || 'ההורדה נכשלה';
        if (friendlyErr.includes('subtitles') && (friendlyErr.includes('429') || friendlyErr.includes('Too Many Requests') || friendlyErr.includes('HTTP Error'))) {
            friendlyErr = 'הורדת הכתוביות נכשלה (חסימת עומס 429 מיוטיוב ⚠️)';
        } else if (friendlyErr.includes('Blocked by NetFree') || friendlyErr.includes('418')) {
            friendlyErr = 'הסרטון חסום בנטפרי 🔒';
        } else if (friendlyErr.includes('unable to extract') || friendlyErr.includes('ffprobe') || friendlyErr.includes('PO Token')) {
            friendlyErr = 'שגיאת פענוח ביוטיוב. יש לעדכן את yt-dlp או לנסות שנית.';
        } else if (friendlyErr.length > 120) {
            friendlyErr = friendlyErr.substring(0, 115) + '...';
        }

        statusDesc.textContent = friendlyErr;
        statusDesc.style.color = '#ef4444';
        speedEl.textContent = '';
    }

    updateDownloadsVisibility();
}

function pauseDownload(downloadId) {
    if (activeDownloads[downloadId]) {
        activeDownloads[downloadId].isPaused = true;
        saveActiveDownloadsToStorage();
    }
    const row = document.getElementById(`item-${downloadId}`);
    if (row) {
        const statusDesc = row.querySelector('.item-status-desc');
        const pauseBtn = row.querySelector('.item-pause-btn');
        const fill = row.querySelector('.progress-fill');
        if (statusDesc) {
            statusDesc.textContent = 'מושהה ⏸';
            statusDesc.style.color = '#f59e0b';
        }
        if (fill) fill.classList.add('paused');
        if (pauseBtn) {
            pauseBtn.classList.add('is-paused');
            pauseBtn.title = 'המשך הורדה';
            pauseBtn.innerHTML = '<span>המשך</span><span>▶</span>';
        }
    }
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'pause_download_advanced',
            downloadId: downloadId
        }));
    }
    updateToggleAllPauseBtnState();
}

function resumeDownload(downloadId) {
    if (activeDownloads[downloadId]) {
        activeDownloads[downloadId].isPaused = false;
        saveActiveDownloadsToStorage();
    }
    const row = document.getElementById(`item-${downloadId}`);
    if (row) {
        const statusDesc = row.querySelector('.item-status-desc');
        const pauseBtn = row.querySelector('.item-pause-btn');
        const fill = row.querySelector('.progress-fill');
        if (statusDesc) {
            statusDesc.textContent = 'מתחבר מחדש...';
            statusDesc.style.color = '';
        }
        if (fill) fill.classList.remove('paused');
        if (pauseBtn) {
            pauseBtn.classList.remove('is-paused');
            pauseBtn.title = 'השהה הורדה';
            pauseBtn.innerHTML = '<span>השהה</span><span>⏸</span>';
        }
    }
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'resume_download_advanced',
            downloadId: downloadId
        }));
    }
    updateToggleAllPauseBtnState();
}

function toggleDownloadPause(downloadId) {
    const item = activeDownloads[downloadId];
    if (item && item.isPaused) {
        resumeDownload(downloadId);
    } else {
        pauseDownload(downloadId);
    }
}

function toggleAllDownloadsPause() {
    const ids = Object.keys(activeDownloads);
    if (ids.length === 0) return;

    // If at least one download is active (not paused), pause all; otherwise resume all
    const hasRunning = ids.some(id => !activeDownloads[id].isPaused);
    if (hasRunning) {
        pauseAllDownloads();
    } else {
        resumeAllDownloads();
    }
}

function pauseAllDownloads() {
    Object.keys(activeDownloads).forEach(id => {
        activeDownloads[id].isPaused = true;
        const row = document.getElementById(`item-${id}`);
        if (row) {
            const statusDesc = row.querySelector('.item-status-desc');
            const pauseBtn = row.querySelector('.item-pause-btn');
            const fill = row.querySelector('.progress-fill');
            if (statusDesc) {
                statusDesc.textContent = 'מושהה ⏸';
                statusDesc.style.color = '#f59e0b';
            }
            if (fill) fill.classList.add('paused');
            if (pauseBtn) {
                pauseBtn.classList.add('is-paused');
                pauseBtn.title = 'המשך הורדה';
                pauseBtn.innerHTML = '<span>המשך</span><span>▶</span>';
            }
        }
    });
    saveActiveDownloadsToStorage();

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'pause_all_downloads_advanced'
        }));
    }
    updateToggleAllPauseBtnState();
}

function resumeAllDownloads() {
    Object.keys(activeDownloads).forEach(id => {
        activeDownloads[id].isPaused = false;
        const row = document.getElementById(`item-${id}`);
        if (row) {
            const statusDesc = row.querySelector('.item-status-desc');
            const pauseBtn = row.querySelector('.item-pause-btn');
            const fill = row.querySelector('.progress-fill');
            if (statusDesc) {
                statusDesc.textContent = 'מתחבר מחדש...';
                statusDesc.style.color = '';
            }
            if (fill) fill.classList.remove('paused');
            if (pauseBtn) {
                pauseBtn.classList.remove('is-paused');
                pauseBtn.title = 'השהה הורדה';
                pauseBtn.innerHTML = '<span>השהה</span><span>⏸</span>';
            }
        }
    });
    saveActiveDownloadsToStorage();

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'resume_all_downloads_advanced'
        }));
    }
    updateToggleAllPauseBtnState();
}

function updateToggleAllPauseBtnState() {
    if (!toggleAllPauseBtn) return;
    const ids = Object.keys(activeDownloads);
    if (ids.length === 0) return;

    const allPaused = ids.every(id => activeDownloads[id].isPaused);
    const iconEl = toggleAllPauseBtn.querySelector('.toggle-all-icon');
    const textEl = toggleAllPauseBtn.querySelector('.toggle-all-text');

    if (allPaused) {
        toggleAllPauseBtn.classList.add('is-paused');
        toggleAllPauseBtn.title = 'המשך את כל ההורדות שבתור';
        if (iconEl) iconEl.textContent = '▶';
        if (textEl) textEl.textContent = 'המשך הכל';
    } else {
        toggleAllPauseBtn.classList.remove('is-paused');
        toggleAllPauseBtn.title = 'השהה את כל ההורדות שבתור';
        if (iconEl) iconEl.textContent = '⏸';
        if (textEl) textEl.textContent = 'השהה הכל';
    }
}

function cancelDownload(downloadId) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'cancel_download_advanced',
            downloadId: downloadId
        }));
        // showToast('🛑 בקשת ביטול נשלחה');
    }
    const row = document.getElementById(`item-${downloadId}`);
    if (row) {
        const statusDesc = row.querySelector('.item-status-desc');
        if (statusDesc) {
            statusDesc.textContent = 'מבטל...';
            statusDesc.style.color = '#fca5a5';
        }
        setTimeout(() => {
            delete activeDownloads[downloadId];
            row.remove();
            updateDownloadsVisibility();
            saveActiveDownloadsToStorage();
        }, 1500);
    }
}

function cancelAllDownloads() {
    if (Object.keys(activeDownloads).length === 0) return;

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
            type: 'cancel_all_downloads_advanced'
        }));
    }

    // Mark all active cards as cancelling
    Object.keys(activeDownloads).forEach(id => {
        const row = document.getElementById(`item-${id}`);
        if (row) {
            const statusDesc = row.querySelector('.item-status-desc');
            if (statusDesc) {
                statusDesc.textContent = 'מבטל...';
                statusDesc.style.color = '#fca5a5';
            }
        }
    });

    setTimeout(() => {
        activeDownloads = {};
        if (activeDownloadsList) activeDownloadsList.innerHTML = '';
        updateDownloadsVisibility();
        saveActiveDownloadsToStorage();
    }, 1200);
}

function renderDownloadItem(item) {
    if (document.getElementById(`item-${item.id}`)) return;

    downloadsSection.classList.remove('hidden');
    const isPaused = item.isPaused === true;
    const div = document.createElement('div');
    div.id = `item-${item.id}`;
    div.className = 'download-item';
    div.innerHTML = `
        <div class="download-item-top">
            <img class="item-thumb" src="${item.thumbnail}" alt="Thumb" onerror="this.src='icon.png'">
            <div class="item-meta">
                <span class="item-title">${item.title}</span>
                <div class="item-status-row">
                    <div>
                        <span class="item-percent-badge">${item.percent}%</span>
                        <span class="item-status-desc" style="margin-inline-start: 4px;">${isPaused ? 'מושהה ⏸' : 'מוריד...'}</span>
                        <span class="item-speed" style="margin-inline-start: 4px;">${item.speed ? `(${item.speed})` : ''}</span>
                    </div>
                </div>
            </div>
            <div class="item-actions">
                <button class="item-pause-btn ${isPaused ? 'is-paused' : ''}" title="${isPaused ? 'המשך הורדה' : 'השהה הורדה'}">
                    <span>${isPaused ? 'המשך' : 'השהה'}</span>
                    <span>${isPaused ? '▶' : '⏸'}</span>
                </button>
                <button class="item-cancel-btn" title="בטל הורדה">
                    <span>ביטול</span>
                    <span>✕</span>
                </button>
            </div>
        </div>
        <div class="progress-container">
            <div class="progress-fill ${isPaused ? 'paused' : ''}" style="width: ${item.percent}%"></div>
        </div>
    `;

    const pauseBtn = div.querySelector('.item-pause-btn');
    const cancelBtn = div.querySelector('.item-cancel-btn');
    if (pauseBtn) pauseBtn.addEventListener('click', () => toggleDownloadPause(item.id));
    if (cancelBtn) cancelBtn.addEventListener('click', () => cancelDownload(item.id));

    activeDownloadsList.prepend(div);
    updateDownloadsVisibility();
    updateToggleAllPauseBtnState();
}

function updateDownloadsVisibility() {
    const count = Object.keys(activeDownloads).length;
    activeCountBadge.textContent = count;
    if (count > 0) {
        downloadsSection.classList.remove('hidden');
        updateToggleAllPauseBtnState();
    } else {
        downloadsSection.classList.add('hidden');
    }
}

// Playlist Management Functions
function addVideoToPlaylist(url, title, thumbnail, domain, channel = '') {
    // Check if video already exists in playlist
    const existingIndex = videoPlaylist.findIndex(video => video.url === url);
    if (existingIndex !== -1) {
        // Show inline error in input
        urlInput.style.borderColor = '#ef4444';
        urlInput.placeholder = '⚠️ הסרטון כבר קיים ברשימה';
        setTimeout(() => {
            urlInput.style.borderColor = '';
            urlInput.placeholder = 'הדבק קישור להוספה לרשימה...';
        }, 2000);
        return;
    }

    const video = {
        id: Date.now().toString(36) + Math.random().toString(36).substr(2),
        url: url,
        title: title || 'סרטון לא ידוע',
        thumbnail: thumbnail || 'icon.png',
        domain: domain || 'Unknown',
        channel: channel || ''
    };

    videoPlaylist.push(video);
    savePlaylistToStorage();
    renderPlaylist();
    // Show inline success in input
    urlInput.style.borderColor = '#10b981';
    urlInput.placeholder = '✓ נוסף לרשימה';
    setTimeout(() => {
        urlInput.style.borderColor = '';
        urlInput.placeholder = 'הדבק קישור להוספה לרשימה...';
    }, 1500);
}

function removeVideoFromPlaylist(videoId) {
    const index = videoPlaylist.findIndex(video => video.id === videoId);
    if (index !== -1) {
        videoPlaylist.splice(index, 1);
        savePlaylistToStorage();
        renderPlaylist();
    }
}

function clearPlaylist() {
    videoPlaylist = [];
    savePlaylistToStorage();
    renderPlaylist();
    // Show inline message in playlist section
    const playlistHeader = document.querySelector('.playlist-header');
    if (playlistHeader) {
        const msg = document.createElement('span');
        msg.textContent = '🗑️ הרשימה נוקתה';
        msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
        playlistHeader.appendChild(msg);
        setTimeout(() => msg.remove(), 2000);
    }
}

function savePlaylistToStorage() {
    chrome.storage.local.set({ videoPlaylist: videoPlaylist }, () => {
        if (chrome.runtime.lastError) {
            console.error('Error saving playlist:', chrome.runtime.lastError);
        }
    });
}

function loadPlaylistFromStorage() {
    chrome.storage.local.get(['videoPlaylist'], (result) => {
        if (result.videoPlaylist && Array.isArray(result.videoPlaylist)) {
            videoPlaylist = result.videoPlaylist;
            renderPlaylist();
        }
    });
}

function saveActiveDownloadsToStorage() {
    chrome.storage.local.set({ activeDownloads: activeDownloads }, () => {
        if (chrome.runtime.lastError) {
            console.error('Error saving active downloads:', chrome.runtime.lastError);
        }
    });
}

function loadActiveDownloadsFromStorage() {
    chrome.storage.local.get(['activeDownloads'], (result) => {
        if (result.activeDownloads && typeof result.activeDownloads === 'object') {
            activeDownloads = result.activeDownloads;
            Object.values(activeDownloads).forEach(item => {
                renderDownloadItem(item);
            });
            updateDownloadsVisibility();
        }
    });
}

async function downloadSingleVideo(videoId) {
    const video = videoPlaylist.find(v => v.id === videoId);
    if (!video) return;

    if (!ws || ws.readyState !== WebSocket.OPEN) {
        // showToast('⚠️ השרת אינו מחובר. מפעיל את התוכנה ברקע...');
        startServer();
        let checkTimer = setInterval(async () => {
            if (ws && ws.readyState === WebSocket.OPEN) {
                clearInterval(checkTimer);
                proceedSingleDownload(video);
            }
        }, 500);
        setTimeout(() => clearInterval(checkTimer), 6000);
        return;
    }

    proceedSingleDownload(video);
}

function cleanSongQuery(title) {
    if (!title) return '';
    let cleaned = title;
    cleaned = cleaned.replace(/\s*-\s*YouTube$/i, '');
    cleaned = cleaned.replace(/[([{\-]\s*(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי|מילים|אודיו|גרסת\s*אולפן)\s*[)\]}]/gi, '');
    cleaned = cleaned.replace(/(?:official\s*(?:music\s*)?video|music\s*video|official\s*audio|video\s*clip|clip\s*officiel|lyric\s*video|lyrics\s*video|official\s*lyrics|4k|hd|1080p|קליפ\s*רשמי|קליפ|אודיו\s*רשמי)\b/gi, '');
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
        console.error('Error resolving clean audio version in popup:', e);
    }
    return null;
}

async function translateTextsToHebrew(texts) {
    if (!texts || texts.length === 0) return [];
    try {
        const delimiter = "\n[[--SEP--]]\n";
        const combined = texts.join(delimiter);
        const res = await fetch(`https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=he&dt=t&q=${encodeURIComponent(combined)}`);
        if (res.ok) {
            const data = await res.json();
            if (data && data[0] && Array.isArray(data[0])) {
                const fullTranslated = data[0].map(item => item[0]).join('');
                const splitParts = fullTranslated.split(/\[\[--SEP--\]\]|\n\[\[--SEP--\]\]\n/);
                if (splitParts.length === texts.length) {
                    return splitParts.map(s => s.trim());
                }
            }
        }
    } catch (e) { }

    const fallbackResults = await Promise.all(texts.map(async txt => {
        try {
            const r = await fetch(`https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=he&dt=t&q=${encodeURIComponent(txt)}`);
            if (r.ok) {
                const d = await r.json();
                if (d && d[0] && Array.isArray(d[0])) {
                    return d[0].map(item => item[0]).join('').trim() || txt;
                }
            }
        } catch (err) { }
        return txt;
    }));
    return fallbackResults;
}

function isMostlyHebrew(text) {
    if (!text) return false;
    const hebrewMatches = text.match(/[\u0590-\u05FF]/g);
    return hebrewMatches && hebrewMatches.length > 3;
}

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

async function fetchAndDownloadSubtitlesFromUrl(videoUrl, videoTitle) {
    try {
        if (!videoUrl || (!videoUrl.includes('youtube.com') && !videoUrl.includes('youtu.be'))) {
            return false;
        }

        const videoIdMatch = videoUrl.match(/[?&]v=([^&]+)/) || videoUrl.match(/youtu\.be\/([^?]+)/);
        const videoId = videoIdMatch ? videoIdMatch[1] : null;

        let captionTracks = null;

        // Strategy 1: YouTube Internal Player API
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

        // Strategy 2: Direct Page Fetch with Balanced JSON Extraction
        if (!captionTracks || captionTracks.length === 0) {
            try {
                const resp = await fetch(videoUrl, {
                    headers: {
                        'Accept-Language': 'he,en;q=0.9',
                        'User-Agent': navigator.userAgent
                    }
                });
                if (resp.ok) {
                    const html = await resp.text();
                    const parsed = extractJsonFromText(html, 'ytInitialPlayerResponse') || extractJsonFromText(html, 'var ytInitialPlayerResponse');
                    captionTracks = parsed?.captions?.playerCaptionsTracklistRenderer?.captionTracks;
                }
            } catch (e) { }
        }

        if (!captionTracks || captionTracks.length === 0) return false;

        const storage = await chrome.storage.local.get(['subsLangDefault']);
        const prefLang = storage?.subsLangDefault || 'he';

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

        let srtContent = "";

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
                const enEntries = await fetchTrackEntries(enTrack);
                if (enEntries.length > 0) {
                    srtContent = buildSrtFromEntries(enEntries);
                }
            }
        }

        if (!srtContent || srtContent.trim().length === 0) return false;

        const safeTitle = (videoTitle || 'subtitles').replace(/[\\/:*?"<>|]/g, '_').trim();
        const dataUrl = 'data:text/plain;charset=utf-8,' + encodeURIComponent(srtContent);
        
        chrome.downloads.download({
            url: dataUrl,
            filename: `${safeTitle}.srt`,
            saveAs: false
        }, () => { });

        return true;
    } catch (e) {
        console.error('Error fetching subtitles in popup:', e);
        return false;
    }
}

async function proceedSingleDownload(video) {
    let finalUrl = video.url;
    let finalTitle = video.title;
    let finalThumbnail = video.thumbnail;
    let originalChannel = video.channel || '';

    const storage = await chrome.storage.local.get(['customSavePath', 'downloadSubsDefault', 'subsLangDefault', 'subsTypeDefault', 'preferCleanAudio', 'customTagsEnabled', 'tagMappings']);
    const isVideo = currentFormatType === 'mp4';
    const cleanAudioEnabled = storage.preferCleanAudio !== false;

    const isYouTube = finalUrl && (finalUrl.includes('youtube.com') || finalUrl.includes('youtu.be'));
    const cleanPromise = (!isVideo && cleanAudioEnabled && isYouTube)
        ? findCleanAudioVersion(finalUrl, finalTitle, originalChannel)
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

    const downloadId = Date.now().toString(36) + Math.random().toString(36).substr(2);
    const selectedQuality = isVideo ? selectedMp4Quality : selectedMp3Quality;

    if (storage.downloadSubsDefault === true) {
        fetchAndDownloadSubtitlesFromUrl(video.url || finalUrl, finalTitle);
    }

    const msg = {
        type: isVideo ? 'download_video_advanced' : 'download_advanced',
        downloadId: downloadId,
        url: finalUrl,
        directUrl: null,
        customTitle: finalTitle,
        customThumbnail: finalThumbnail,
        customSavePath: storage.customSavePath || null,
        formatId: selectedQuality,
        playlist: false,
        qualityText: isVideo ? 'וידאו (MP4)' : 'שמע (MP3) - גרסת אולפן',
        cookies: cookies || null,
        downloadSubs: false, // Handled cleanly via browser without causing 429 abort
        subsLang: storage.subsLangDefault || 'he',
        subsType: storage.subsTypeDefault || 'separate',
        tagMappings: storage.customTagsEnabled ? (storage.tagMappings || null) : null
    };

    activeDownloads[downloadId] = {
        id: downloadId,
        title: finalTitle,
        thumbnail: finalThumbnail,
        percent: '0',
        speed: ''
    };
    renderDownloadItem(activeDownloads[downloadId]);
    saveActiveDownloadsToStorage();

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(msg));
    }
}

function renderPlaylist() {
    playlistList.innerHTML = '';
    playlistCountBadge.textContent = videoPlaylist.length;

    if (videoPlaylist.length === 0) {
        playlistSection.classList.add('hidden');
        return;
    }

    playlistSection.classList.remove('hidden');

    videoPlaylist.forEach(video => {
        const div = document.createElement('div');
        div.className = 'playlist-item';
        div.innerHTML = `
            <div class="playlist-item-top">
                <img class="playlist-thumb" src="${video.thumbnail}" alt="Thumb" onerror="this.src='icon.png'">
                <div class="playlist-meta">
                    <span class="playlist-title">${video.title}</span>
                    <span class="playlist-domain">${video.domain}</span>
                </div>
                <div class="playlist-item-actions">
                    <button class="playlist-download-btn" data-id="${video.id}" title="הורד">
                        <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                            <polyline points="7 10 12 15 17 10"></polyline>
                            <line x1="12" y1="15" x2="12" y2="3"></line>
                        </svg>
                    </button>
                    <button class="playlist-remove-btn" data-id="${video.id}" title="הסר מהרשימה">
                        <span>✕</span>
                    </button>
                </div>
            </div>
        `;

        const downloadBtn = div.querySelector('.playlist-download-btn');
        downloadBtn.addEventListener('click', () => downloadSingleVideo(video.id));

        const removeBtn = div.querySelector('.playlist-remove-btn');
        removeBtn.addEventListener('click', () => removeVideoFromPlaylist(video.id));

        playlistList.appendChild(div);
    });
}

// Helper to normalize strings for deduplication by title/similarity
function normalizeTitle(str) {
    if (!str) return '';
    return str.toLowerCase()
        .replace(/[\(\[\{][^\)\]\}]*[\)\]\}]/g, '') // remove bracket contents like (Official Music Video), [Audio], etc.
        .replace(/official\s*(music\s*)?video/gi, '')
        .replace(/קליפ\s*רשמי/g, '')
        .replace(/שיר\s*רשמי/g, '')
        .replace(/אודיו\s*רשמי/g, '')
        .replace(/lyric\s*video/gi, '')
        .replace(/מילים/g, '')
        .replace(/[^\p{L}\p{N}\s]/gu, '') // keep letters, numbers, spaces
        .replace(/\s+/g, ' ')
        .trim();
}

// Helper to fetch full official discography of an artist via iTunes Search API (Free, fast & comprehensive)
async function fetchArtistDiscographyFromItunes(query) {
    try {
        // Step 1: Find the exact artist entity first to get their official artistId
        const artistLookupUrl = `https://itunes.apple.com/search?term=${encodeURIComponent(query)}&entity=musicArtist&limit=5`;
        const artistResp = await fetch(artistLookupUrl);
        let targetArtistId = null;
        let canonicalArtistName = query;

        if (artistResp.ok) {
            const aData = await artistResp.json();
            if (aData.results && aData.results.length > 0) {
                // Find best matching artist
                const cleanQuery = query.toLowerCase().trim();
                const match = aData.results.find(a => (a.artistName || '').toLowerCase().includes(cleanQuery)) || aData.results[0];
                if (match) {
                    targetArtistId = match.artistId;
                    canonicalArtistName = match.artistName;
                }
            }
        }

        let tracksData = [];
        if (targetArtistId) {
            // Lookup all official tracks by this exact artist ID (excludes covers/tributes)
            const lookupUrl = `https://itunes.apple.com/lookup?id=${targetArtistId}&entity=song&limit=200`;
            const lResp = await fetch(lookupUrl);
            if (lResp.ok) {
                const lData = await lResp.json();
                tracksData = (lData.results || []).filter(item => item.wrapperType === 'track');
            }
        }

        // Fallback to strict song search if lookup returned empty
        if (tracksData.length === 0) {
            const itunesUrl = `https://itunes.apple.com/search?term=${encodeURIComponent(query)}&entity=song&attribute=artistTerm&limit=200`;
            const resp = await fetch(itunesUrl);
            if (resp.ok) {
                const data = await resp.json();
                const cleanQ = query.toLowerCase().trim();
                tracksData = (data.results || []).filter(item => {
                    const aName = (item.artistName || '').toLowerCase();
                    // Exclude tribute bands, piano covers, instrumental covers
                    return aName.includes(cleanQ) && !aName.includes('cover') && !aName.includes('piano') && !aName.includes('tribute') && !aName.includes('karaoke');
                });
            }
        }

        if (tracksData.length === 0) return [];
        
        const songs = [];
        const seenNames = new Set();

        for (const item of tracksData) {
            const trackName = item.trackName || '';
            const artistName = item.artistName || canonicalArtistName;
            const norm = normalizeTitle(trackName);
            if (!norm || seenNames.has(norm)) continue;
            seenNames.add(norm);

            // Format duration mm:ss
            let durationStr = '';
            if (item.trackTimeMillis) {
                const totalSec = Math.floor(item.trackTimeMillis / 1000);
                const min = Math.floor(totalSec / 60);
                const sec = totalSec % 60;
                durationStr = `${min}:${sec < 10 ? '0' : ''}${sec}`;
            }

            const artwork = (item.artworkUrl100 || '').replace('100x100bb', '300x300bb');

            songs.push({
                title: `${artistName} - ${trackName}`,
                cleanTitle: `${artistName} ${trackName}`,
                artist: artistName,
                trackName: trackName,
                thumbnail: artwork || 'icon.png',
                duration: durationStr
            });
        }
        return songs;
    } catch (e) {
        console.error('iTunes discography error:', e);
        return [];
    }
}

// Quick resolver to find best YouTube video ID for a specific song title on the official artist channel/topic
async function resolveYouTubeVideoForSong(artistName, trackName) {
    try {
        const cleanArtist = artistName.toLowerCase().trim();
        const searchQuery = `"${artistName}" "${trackName}"`;
        const searchUrl = `https://www.youtube.com/results?search_query=${encodeURIComponent(searchQuery)}&sp=EgIQAQ%253D%253D`;
        const resp = await fetch(searchUrl, {
            headers: {
                'Accept-Language': 'he,en;q=0.9',
                'User-Agent': navigator.userAgent
            }
        });
        const html = await resp.text();
        const parsed = parseYouTubeSearchData(html);
        if (parsed.items && parsed.items.length > 0) {
            // Filter out piano covers / tutorials / reaction videos
            const filteredItems = parsed.items.filter(item => {
                const titleLower = (item.title || '').toLowerCase();
                const chLower = (item.channel || '').toLowerCase();
                if (titleLower.includes('piano cover') || titleLower.includes('tutorial') || titleLower.includes('reaction') || titleLower.includes('karaoke')) return false;
                if (chLower.includes('piano') || chLower.includes('cover') || chLower.includes('karaoke')) return false;
                return true;
            });

            const candidateList = filteredItems.length > 0 ? filteredItems : parsed.items;

            // 1. Best priority: channel matches artist name or Topic channel
            const officialMatch = candidateList.find(item => {
                const ch = (item.channel || '').toLowerCase();
                return ch.includes(cleanArtist) || ch.includes('topic') || ch.includes('נושא') || ch.includes('vevo');
            });

            if (officialMatch) {
                return officialMatch;
            }

            return candidateList[0];
        }
    } catch (e) {
        console.error('Resolve error:', e);
    }
    return null;
}

// Artist / Video Search Functions
async function searchYouTubeArtist(query, maxResults = 10) {
    if (!query) return;
    
    const isAll = maxResults === 'all' || maxResults >= 9999;
    const targetCount = isAll ? 500 : parseInt(maxResults, 10);
    
    searchResultsSection.classList.remove('hidden');
    searchResultsList.innerHTML = `
        <div class="search-loading-state">
            <div class="search-spinner"></div>
            <span>${isAll ? `סורק דיסקוגרפיה רשמית מלאה עבור "${query}"...` : `מחפש ${targetCount} שירים מובילים עבור "${query}"...`}</span>
        </div>
    `;
    resultsCountBadge.textContent = '...';

    try {
        let allItems = [];
        const seenIds = new Set();
        const seenTitles = new Set();

        // Helper to add unique item
        const addUniqueItem = (item) => {
            if (!item || !item.id) return false;
            if (seenIds.has(item.id)) return false;
            
            const norm = normalizeTitle(item.title);
            if (norm && seenTitles.has(norm)) {
                return false;
            }

            seenIds.add(item.id);
            if (norm) seenTitles.add(norm);
            allItems.push(item);
            return true;
        };

        // 1. If "All" is selected -> Fetch full official discography from iTunes first
        if (isAll) {
            const discography = await fetchArtistDiscographyFromItunes(query);
            if (discography.length > 0) {
                searchResultsList.innerHTML = `
                    <div class="search-loading-state">
                        <div class="search-spinner"></div>
                        <span>נמצאו ${discography.length} שירים רשמיים! מאתר ערוצים רשמיים...</span>
                    </div>
                `;

                // Map discography songs to working YouTube links
                const batchSize = 6;
                for (let i = 0; i < discography.length; i += batchSize) {
                    const batch = discography.slice(i, i + batchSize);
                    const resolvedBatch = await Promise.all(
                        batch.map(async (song) => {
                            const ytMatch = await resolveYouTubeVideoForSong(song.artist, song.trackName);
                            if (ytMatch) {
                                return {
                                    id: ytMatch.id,
                                    url: ytMatch.url,
                                    title: song.title,
                                    channel: ytMatch.channel || song.artist,
                                    thumbnail: ytMatch.thumbnail || song.thumbnail,
                                    duration: ytMatch.duration || song.duration
                                };
                            }
                            return null;
                        })
                    );

                    for (const item of resolvedBatch) {
                        if (item) addUniqueItem(item);
                    }

                    // Update UI progress live
                    resultsCountBadge.textContent = `${allItems.length}`;
                }
            }
        } else {
            // 2. Direct YouTube search (Only used for limited count 5/10/20/30)
            const searchUrl = `https://www.youtube.com/results?search_query=${encodeURIComponent(query + ' official music')}&sp=EgIQAQ%253D%253D`;
            const response = await fetch(searchUrl, {
                headers: {
                    'Accept-Language': 'he,en;q=0.9',
                    'User-Agent': navigator.userAgent
                }
            });

            const html = await response.text();
            const parsed = parseYouTubeSearchData(html);
            (parsed.items || []).forEach(addUniqueItem);
            let continuationToken = parsed.continuationToken;

            let attempts = 0;
            const maxAttempts = 5;
            while (allItems.length < targetCount && continuationToken && attempts < maxAttempts) {
                attempts++;
                try {
                    const contResp = await fetch(`https://www.youtube.com/youtubei/v1/search?prettyPrint=false`, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            context: {
                                client: {
                                    clientName: 'WEB',
                                    clientVersion: '2.20240101.00.00',
                                    hl: 'he',
                                    gl: 'IL'
                                }
                            },
                            continuation: continuationToken
                        })
                    });

                    if (!contResp.ok) break;
                    const contData = await contResp.json();
                    const contParsed = extractVideosFromContinuation(contData);
                    if (contParsed.items && contParsed.items.length > 0) {
                        for (const item of contParsed.items) {
                            addUniqueItem(item);
                        }
                        continuationToken = contParsed.continuationToken;
                    } else {
                        break;
                    }
                } catch (e) {
                    console.error('Continuation error:', e);
                    break;
                }
            }
        }

        // Limit to requested count if not 'all'
        const finalResults = isAll ? allItems : allItems.slice(0, targetCount);

        if (finalResults.length === 0) {
            searchResultsList.innerHTML = `
                <div class="search-loading-state" style="color: var(--yt-spec-text-secondary);">
                    לא נמצאו שירים עבור "${query}". נסה חיפוש אחר.
                </div>
            `;
            resultsCountBadge.textContent = '0';
            searchResults = [];
            return;
        }

        searchResults = finalResults.map((item) => ({
            ...item,
            selected: true // Selected by default
        }));

        renderSearchResults();
    } catch (err) {
        console.error('Search error:', err);
        searchResultsList.innerHTML = `
            <div class="search-loading-state" style="color: #ef4444;">
                שגיאה בביצוע החיפוש. נסה שוב.
            </div>
        `;
        resultsCountBadge.textContent = '0';
    }
}

// Helper to find continuation token anywhere in an object tree
function findContinuationToken(obj) {
    if (!obj || typeof obj !== 'object') return null;
    if (obj.continuationCommand && obj.continuationCommand.token) {
        return obj.continuationCommand.token;
    }
    if (obj.continuationEndpoint && obj.continuationEndpoint.continuationCommand && obj.continuationEndpoint.continuationCommand.token) {
        return obj.continuationEndpoint.continuationCommand.token;
    }
    for (const key of Object.keys(obj)) {
        if (typeof obj[key] === 'object') {
            const found = findContinuationToken(obj[key]);
            if (found) return found;
        }
    }
    return null;
}

// Parse initial data JSON embedded in YouTube search HTML
function parseYouTubeSearchData(html) {
    const items = [];
    let continuationToken = null;

    try {
        let jsonStr = '';
        const match = html.match(/var ytInitialData = ({.*?});<\/script>/s) || html.match(/ytInitialData\s*=\s*({.*?});/s);
        if (match && match[1]) {
            jsonStr = match[1];
        }

        if (jsonStr) {
            const data = JSON.parse(jsonStr);
            
            // Extract continuation token
            continuationToken = findContinuationToken(data);

            // Extract videos from contents
            const contents = data?.contents?.twoColumnSearchResultsRenderer?.primaryContents?.sectionListRenderer?.contents;
            if (Array.isArray(contents)) {
                for (const section of contents) {
                    const itemSection = section?.itemSectionRenderer?.contents;
                    if (Array.isArray(itemSection)) {
                        for (const item of itemSection) {
                            const videoRenderer = item.videoRenderer;
                            if (videoRenderer && videoRenderer.videoId) {
                                const videoId = videoRenderer.videoId;
                                const title = videoRenderer.title?.runs?.[0]?.text || videoRenderer.title?.simpleText || 'ללא כותרת';
                                const channel = videoRenderer.ownerText?.runs?.[0]?.text || videoRenderer.shortBylineText?.runs?.[0]?.text || 'אמן';
                                const thumbnail = videoRenderer.thumbnail?.thumbnails?.slice(-1)[0]?.url || `https://i.ytimg.com/vi/${videoId}/hqdefault.jpg`;
                                const duration = videoRenderer.lengthText?.simpleText || '';

                                items.push({
                                    id: videoId,
                                    url: `https://www.youtube.com/watch?v=${videoId}`,
                                    title: title,
                                    channel: channel,
                                    thumbnail: thumbnail,
                                    duration: duration
                                });
                            }
                        }
                    }
                }
            }
        }
    } catch (e) {
        console.error('Failed to parse ytInitialData:', e);
    }

    // Fallback: simple regex matching for videoId and title
    if (items.length === 0) {
        const regex = /\/watch\?v=([a-zA-Z0-9_-]{11})/g;
        const matches = [...html.matchAll(regex)];
        const seen = new Set();
        for (const m of matches) {
            const vid = m[1];
            if (!seen.has(vid)) {
                seen.add(vid);
                items.push({
                    id: vid,
                    url: `https://www.youtube.com/watch?v=${vid}`,
                    title: `סרטון (${vid})`,
                    channel: 'YouTube',
                    thumbnail: `https://i.ytimg.com/vi/${vid}/hqdefault.jpg`,
                    duration: ''
                });
            }
        }
    }

    return { items, continuationToken };
}

function extractVideosFromContinuation(data) {
    const items = [];
    let continuationToken = findContinuationToken(data);

    try {
        // Recursively extract all videoRenderers in data
        const findVideos = (obj) => {
            if (!obj || typeof obj !== 'object') return;
            if (obj.videoRenderer && obj.videoRenderer.videoId) {
                const vr = obj.videoRenderer;
                const videoId = vr.videoId;
                const title = vr.title?.runs?.[0]?.text || vr.title?.simpleText || 'ללא כותרת';
                const channel = vr.ownerText?.runs?.[0]?.text || vr.shortBylineText?.runs?.[0]?.text || 'אמן';
                const thumbnail = vr.thumbnail?.thumbnails?.slice(-1)[0]?.url || `https://i.ytimg.com/vi/${videoId}/hqdefault.jpg`;
                const duration = vr.lengthText?.simpleText || '';

                items.push({
                    id: videoId,
                    url: `https://www.youtube.com/watch?v=${videoId}`,
                    title: title,
                    channel: channel,
                    thumbnail: thumbnail,
                    duration: duration
                });
                return;
            }
            for (const key of Object.keys(obj)) {
                if (typeof obj[key] === 'object') {
                    findVideos(obj[key]);
                }
            }
        };

        findVideos(data);
    } catch (e) {
        console.error('Error in extractVideosFromContinuation:', e);
    }

    return { items, continuationToken };
}

function renderSearchResults() {
    searchResultsList.innerHTML = '';
    const selectedCount = searchResults.filter(i => i.selected).length;
    resultsCountBadge.textContent = `${selectedCount}/${searchResults.length}`;
    selectAllResultsCb.checked = selectedCount === searchResults.length && searchResults.length > 0;
    selectAllResultsCb.indeterminate = selectedCount > 0 && selectedCount < searchResults.length;

    searchResults.forEach((item, index) => {
        const row = document.createElement('div');
        row.className = `search-item ${item.selected ? 'selected' : ''}`;
        row.innerHTML = `
            <div class="search-item-checkbox">
                <input type="checkbox" data-index="${index}" ${item.selected ? 'checked' : ''}>
            </div>
            <img class="search-item-thumb" src="${item.thumbnail}" alt="Thumb" onerror="this.src='icon.png'">
            <div class="search-item-meta">
                <span class="search-item-title" title="${item.title}">${item.title}</span>
                <span class="search-item-channel">${item.channel} ${item.duration ? `• ${item.duration}` : ''}</span>
            </div>
        `;

        const cb = row.querySelector('input[type="checkbox"]');
        cb.addEventListener('change', (e) => {
            e.stopPropagation();
            item.selected = cb.checked;
            row.classList.toggle('selected', item.selected);
            updateSearchResultsHeader();
        });

        row.addEventListener('click', (e) => {
            if (e.target.tagName !== 'INPUT') {
                cb.checked = !cb.checked;
                item.selected = cb.checked;
                row.classList.toggle('selected', item.selected);
                updateSearchResultsHeader();
            }
        });

        searchResultsList.appendChild(row);
    });
}

function updateSearchResultsHeader() {
    const selectedCount = searchResults.filter(i => i.selected).length;
    resultsCountBadge.textContent = `${selectedCount}/${searchResults.length}`;
    selectAllResultsCb.checked = selectedCount === searchResults.length && searchResults.length > 0;
    selectAllResultsCb.indeterminate = selectedCount > 0 && selectedCount < searchResults.length;
}

function addSelectedSearchResultsToPlaylist() {
    const chosen = searchResults.filter(i => i.selected);
    if (chosen.length === 0) {
        // Show inline error in search section
        const resultsHeader = document.querySelector('.results-header');
        if (resultsHeader) {
            const msg = document.createElement('span');
            msg.textContent = '⚠️ לא נבחרו שירים להוספה';
            msg.style.cssText = 'color: #ef4444; font-size: 12px; margin-inline-start: 8px;';
            resultsHeader.appendChild(msg);
            setTimeout(() => msg.remove(), 2000);
        }
        return;
    }

    let addedCount = 0;
    chosen.forEach(item => {
        const exists = videoPlaylist.some(v => v.url === item.url);
        if (!exists) {
            videoPlaylist.push({
                id: Date.now().toString(36) + Math.random().toString(36).substr(2),
                url: item.url,
                title: item.title,
                thumbnail: item.thumbnail,
                domain: 'YouTube',
                channel: item.channel || ''
            });
            addedCount++;
        }
    });

    if (addedCount > 0) {
        savePlaylistToStorage();
        renderPlaylist();
        // Show inline success in search section
        const resultsHeader = document.querySelector('.results-header');
        if (resultsHeader) {
            const msg = document.createElement('span');
            msg.textContent = `✓ נוספו ${addedCount} שירים`;
            msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
            resultsHeader.appendChild(msg);
            setTimeout(() => msg.remove(), 2000);
        }
    } else {
        // Show inline warning in search section
        const resultsHeader = document.querySelector('.results-header');
        if (resultsHeader) {
            const msg = document.createElement('span');
            msg.textContent = '⚠️ כבר קיימים ברשימה';
            msg.style.cssText = 'color: #f59e0b; font-size: 12px; margin-inline-start: 8px;';
            resultsHeader.appendChild(msg);
            setTimeout(() => msg.remove(), 2000);
        }
    }
}

// Auto detect current tab
async function detectCurrentTab() {
    try {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        if (tab && tab.url) {
            const isYt = tab.url.includes('youtube.com/watch') || tab.url.includes('youtu.be/');
            const isNetube = tab.url.includes('netube.co.il');

            if (isYt || isNetube) {
                urlInput.value = tab.url;
                clearBtn.classList.remove('hidden');

                detectedMedia.url = tab.url;
                detectedMedia.title = tab.title ? tab.title.replace(' - YouTube', '') : 'סרטון פעיל';
                detectedMedia.channel = '';

                // Try to get channel name from active tab DOM
                if (isYt && tab.id) {
                    try {
                        const results = await chrome.scripting.executeScript({
                            target: { tabId: tab.id },
                            func: () => {
                                const chEl = document.querySelector('ytd-watch-metadata #owner #channel-name a') ||
                                             document.querySelector('#channel-name a') ||
                                             document.querySelector('ytd-channel-name a') ||
                                             document.querySelector('#owner-name a');
                                return chEl ? chEl.textContent.trim() : '';
                            }
                        });
                        if (results && results[0] && results[0].result) {
                            detectedMedia.channel = results[0].result;
                        }
                    } catch (err) { }
                }
                
                if (isYt && tab.url.includes('v=')) {
                    const vidId = tab.url.split('v=')[1].split('&')[0];
                    detectedMedia.thumbnail = `https://i.ytimg.com/vi/${vidId}/hqdefault.jpg`;
                }

                mediaTitle.textContent = detectedMedia.title;
                if (detectedMedia.thumbnail) {
                    mediaThumb.src = detectedMedia.thumbnail;
                } else {
                    mediaThumb.src = 'icon.png';
                }
                mediaDomain.textContent = isYt ? 'YouTube' : 'Netube';
                // Don't show media preview since we auto-add to playlist instead
            }
        }
    } catch (e) { }
}

// Start Download
async function triggerDownload() {
    const url = urlInput.value.trim();
    if (!url) {
        // Show inline error in input
        urlInput.style.borderColor = '#ef4444';
        urlInput.placeholder = '⚠️ אנא הדבק קישור תקין';
        urlInput.focus();
        setTimeout(() => {
            urlInput.style.borderColor = '';
            urlInput.placeholder = 'הדבק קישור להוספה לרשימה...';
        }, 2000);
        return;
    }

    // Add to playlist instead of directly downloading
    addVideoToPlaylist(
        url,
        detectedMedia.url === url ? detectedMedia.title : null,
        detectedMedia.url === url ? detectedMedia.thumbnail : null,
        detectedMedia.url === url ? (url.includes('youtube.com') || url.includes('youtu.be') ? 'YouTube' : 'Netube') : 'Unknown',
        detectedMedia.url === url ? (detectedMedia.channel || '') : ''
    );

    // Clear the input after adding
    urlInput.value = '';
    clearBtn.classList.add('hidden');
    mediaPreview.classList.add('hidden');
}

// Handle manual URL input (when user types URL and presses Enter)
urlInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        triggerDownload();
    }
});

// Download all videos in playlist
async function downloadAllFromPlaylist() {
    if (videoPlaylist.length === 0) {
        // Show inline error in playlist section
        const playlistHeader = document.querySelector('.playlist-header');
        if (playlistHeader) {
            const msg = document.createElement('span');
            msg.textContent = '⚠️ הרשימה ריקה';
            msg.style.cssText = 'color: #ef4444; font-size: 12px; margin-inline-start: 8px;';
            playlistHeader.appendChild(msg);
            setTimeout(() => msg.remove(), 2000);
        }
        return;
    }

    if (!ws || ws.readyState !== WebSocket.OPEN) {
        // showToast('⚠️ השרת אינו מחובר. מפעיל את התוכנה ברקע...');
        startServer();
        let checkTimer = setInterval(async () => {
            if (ws && ws.readyState === WebSocket.OPEN) {
                clearInterval(checkTimer);
                proceedDownloadAll();
            }
        }, 500);
        setTimeout(() => clearInterval(checkTimer), 6000);
        return;
    }

    proceedDownloadAll();
}

async function proceedDownloadAll() {
    const storage = await chrome.storage.local.get(['customSavePath', 'downloadSubsDefault', 'subsLangDefault', 'subsTypeDefault', 'preferCleanAudio', 'customTagsEnabled', 'tagMappings']);
    const isVideo = currentFormatType === 'mp4';
    const cleanAudioEnabled = storage.preferCleanAudio !== false;

    // Process and dispatch all downloads concurrently in parallel
    const downloadPromises = videoPlaylist.map(async (video) => {
        let finalUrl = video.url;
        let finalTitle = video.title;
        let finalThumbnail = video.thumbnail;
        let originalChannel = video.channel || '';

        const isYouTube = finalUrl && (finalUrl.includes('youtube.com') || finalUrl.includes('youtu.be'));
        const cleanPromise = (!isVideo && cleanAudioEnabled && isYouTube)
            ? findCleanAudioVersion(finalUrl, finalTitle, originalChannel)
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

        const downloadId = Date.now().toString(36) + Math.random().toString(36).substr(2);
        const selectedQuality = isVideo ? selectedMp4Quality : selectedMp3Quality;

        if (storage.downloadSubsDefault === true) {
            fetchAndDownloadSubtitlesFromUrl(video.url || finalUrl, finalTitle);
        }

        const msg = {
            type: isVideo ? 'download_video_advanced' : 'download_advanced',
            downloadId: downloadId,
            url: finalUrl,
            directUrl: null,
            customTitle: finalTitle,
            customThumbnail: finalThumbnail,
            customSavePath: storage.customSavePath || null,
            formatId: selectedQuality,
            playlist: false,
            qualityText: isVideo ? 'וידאו (MP4)' : 'שמע (MP3) - גרסת אולפן',
            cookies: cookies || null,
            downloadSubs: false, // Handled cleanly via browser without causing 429 abort
            subsLang: storage.subsLangDefault || 'he',
            subsType: storage.subsTypeDefault || 'separate',
            tagMappings: storage.customTagsEnabled ? (storage.tagMappings || null) : null
        };

        activeDownloads[downloadId] = {
            id: downloadId,
            title: finalTitle,
            thumbnail: finalThumbnail,
            percent: '0',
            speed: ''
        };
        renderDownloadItem(activeDownloads[downloadId]);

        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(msg));
        }
    });

    await Promise.all(downloadPromises);
    saveActiveDownloadsToStorage();
}

async function proceedDownload(url) {
    let finalUrl = url;
    let finalTitle = detectedMedia.url === url ? detectedMedia.title : null;
    let finalThumbnail = detectedMedia.url === url ? detectedMedia.thumbnail : null;
    let originalChannel = (detectedMedia.url === url && detectedMedia.channel) ? detectedMedia.channel : '';

    const storage = await chrome.storage.local.get(['customSavePath', 'downloadSubsDefault', 'subsLangDefault', 'subsTypeDefault', 'preferCleanAudio', 'customTagsEnabled', 'tagMappings']);
    const isVideo = currentFormatType === 'mp4';
    const cleanAudioEnabled = storage.preferCleanAudio !== false;

    const isYouTube = finalUrl && (finalUrl.includes('youtube.com') || finalUrl.includes('youtu.be'));
    const cleanPromise = (!isVideo && cleanAudioEnabled && isYouTube)
        ? findCleanAudioVersion(finalUrl, finalTitle, originalChannel)
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

    const downloadId = Date.now().toString(36) + Math.random().toString(36).substr(2);
    const selectedQuality = isVideo ? selectedMp4Quality : selectedMp3Quality;

    if (storage.downloadSubsDefault === true) {
        fetchAndDownloadSubtitlesFromUrl(finalUrl, finalTitle);
    }

    const msg = {
        type: isVideo ? 'download_video_advanced' : 'download_advanced',
        downloadId: downloadId,
        url: finalUrl,
        directUrl: null,
        customTitle: finalTitle,
        customThumbnail: finalThumbnail,
        customSavePath: storage.customSavePath || null,
        formatId: selectedQuality,
        playlist: false,
        qualityText: isVideo ? 'וידאו (MP4)' : 'שמע (MP3) - גרסת אולפן',
        cookies: cookies || null,
        downloadSubs: false, // Handled cleanly via browser without causing 429 abort
        subsLang: storage.subsLangDefault || 'he',
        subsType: storage.subsTypeDefault || 'separate',
        tagMappings: storage.customTagsEnabled ? (storage.tagMappings || null) : null
    };

    activeDownloads[downloadId] = {
        id: downloadId,
        title: finalTitle || 'מוריד...',
        thumbnail: finalThumbnail || 'icon.png',
        percent: '0',
        speed: ''
    };
    renderDownloadItem(activeDownloads[downloadId]);
    saveActiveDownloadsToStorage();

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(msg));
    }
}

// Event Listeners
document.addEventListener('DOMContentLoaded', () => {
    connectWebSocket();
    detectCurrentTab();
    updateQualityBadge();
    loadPlaylistFromStorage(); // Load saved playlist
    loadActiveDownloadsFromStorage(); // Load saved active downloads
    
    // Hide media preview initially if no URL
    if (urlInput.value.trim().length === 0) {
        mediaPreview.classList.add('hidden');
    }

    // Load saved settings
    chrome.storage.local.get([
        'userConfig', 
        'savedMp3Quality', 
        'savedMp4Quality', 
        'customSavePath',
        'downloadSubsDefault',
        'subsLangDefault',
        'subsTypeDefault',
        'preferCleanAudio',
        'customTagsEnabled',
        'tagMappings'
    ], (res) => {
        const conf = res.userConfig || {};
        if (genericBtnToggle) genericBtnToggle.checked = conf.enableGenericFloatingBtn || false;

        const cleanAudioToggle = document.getElementById('clean-audio-toggle');
        if (cleanAudioToggle) {
            cleanAudioToggle.checked = res.preferCleanAudio !== false;
            cleanAudioToggle.addEventListener('change', (e) => {
                const enabled = e.target.checked;
                chrome.storage.local.set({ preferCleanAudio: enabled });
                // Show inline message in settings card
                const cleanAudioCard = document.querySelector('.settings-card:nth-child(3)'); // Clean Audio card
                if (cleanAudioCard) {
                    const msg = document.createElement('span');
                    msg.textContent = enabled ? '🎧 הופעל' : '🎧 כובה';
                    msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
                    cleanAudioCard.querySelector('h3').appendChild(msg);
                    setTimeout(() => msg.remove(), 1500);
                }
            });
        }

        if (savePathInput && res.customSavePath) {
            savePathInput.value = res.customSavePath;
        }

        if (res.savedMp3Quality) {
            selectedMp3Quality = res.savedMp3Quality;
            document.querySelectorAll('#mp3-qualities .chip').forEach(c => {
                c.classList.toggle('active', c.getAttribute('data-format') === selectedMp3Quality);
            });
        }
        if (res.savedMp4Quality) {
            selectedMp4Quality = res.savedMp4Quality;
            document.querySelectorAll('#mp4-qualities .chip').forEach(c => {
                c.classList.toggle('active', c.getAttribute('data-format') === selectedMp4Quality);
            });
        }
        updateQualityBadge();

        // Subtitles settings initialization
        const subsToggle = document.getElementById('subs-toggle');
        const subsSubOptions = document.getElementById('subs-sub-options');
        const downloadSubs = res.downloadSubsDefault === true;
        const subsLang = res.subsLangDefault || 'he';
        const subsType = res.subsTypeDefault || 'separate';

        if (subsToggle) {
            subsToggle.checked = downloadSubs;
            if (subsSubOptions) {
                subsSubOptions.classList.toggle('hidden', !downloadSubs);
            }
            subsToggle.addEventListener('change', (e) => {
                const enabled = e.target.checked;
                if (subsSubOptions) subsSubOptions.classList.toggle('hidden', !enabled);
                chrome.storage.local.set({ downloadSubsDefault: enabled });
                // Show inline message in settings card
                const subsCard = document.querySelector('.settings-card:nth-child(4)'); // Subtitles card
                if (subsCard) {
                    const msg = document.createElement('span');
                    msg.textContent = enabled ? '📝 הופעל' : '📝 כובה';
                    msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
                    subsCard.querySelector('h3').appendChild(msg);
                    setTimeout(() => msg.remove(), 1500);
                }
            });
        }

        // Subtitle Language chips
        document.querySelectorAll('#subs-lang-chips .chip').forEach(chip => {
            chip.classList.toggle('active', chip.getAttribute('data-lang') === subsLang);
            chip.addEventListener('click', (e) => {
                const lang = e.currentTarget.getAttribute('data-lang');
                document.querySelectorAll('#subs-lang-chips .chip').forEach(c => c.classList.remove('active'));
                e.currentTarget.classList.add('active');
                chrome.storage.local.set({ subsLangDefault: lang });
            });
        });

        // Subtitle Type chips (separate vs embed)
        document.querySelectorAll('#subs-type-chips .chip').forEach(chip => {
            chip.classList.toggle('active', chip.getAttribute('data-type-opt') === subsType);
            chip.addEventListener('click', (e) => {
                const typeOpt = e.currentTarget.getAttribute('data-type-opt');
                document.querySelectorAll('#subs-type-chips .chip').forEach(c => c.classList.remove('active'));
                e.currentTarget.classList.add('active');
                chrome.storage.local.set({ subsTypeDefault: typeOpt });
            });
        });

        // Tag/Metadata settings initialization
        const customTagsToggle = document.getElementById('custom-tags-toggle');
        const tagsSubOptions = document.getElementById('tags-sub-options');
        const tagsEnabled = res.customTagsEnabled === true;
        const savedTagMappings = res.tagMappings || {};

        if (customTagsToggle) {
            customTagsToggle.checked = tagsEnabled;
            if (tagsSubOptions) {
                tagsSubOptions.classList.toggle('hidden', !tagsEnabled);
            }
            customTagsToggle.addEventListener('change', (e) => {
                const enabled = e.target.checked;
                if (tagsSubOptions) tagsSubOptions.classList.toggle('hidden', !enabled);
                chrome.storage.local.set({ customTagsEnabled: enabled });
            });
        }

        // Tag field selects
        const tagSelects = [
            { id: 'tag-artist-select', key: 'artist' },
            { id: 'tag-album-select', key: 'album' },
            { id: 'tag-year-select', key: 'year' },
            { id: 'tag-comment-select', key: 'comment' }
        ];
        tagSelects.forEach(({ id, key }) => {
            const selectEl = document.getElementById(id);
            if (selectEl) {
                if (savedTagMappings[key]) {
                    selectEl.value = savedTagMappings[key];
                }
                selectEl.addEventListener('change', () => {
                    chrome.storage.local.get(['tagMappings'], (r) => {
                        const mappings = r.tagMappings || {};
                        mappings[key] = selectEl.value;
                        chrome.storage.local.set({ tagMappings: mappings });
                    });
                });
            }
        });
    });

    // Save Path changes
    if (savePathInput) {
        savePathInput.addEventListener('change', () => {
            const val = savePathInput.value.trim();
            chrome.storage.local.set({ customSavePath: val });
            // Show inline message in settings card
            const pathCard = document.querySelector('.settings-card:nth-child(1)'); // Save path card
            if (pathCard) {
                const msg = document.createElement('span');
                msg.textContent = val ? '📁 נשמר' : '📁 ברירת מחדל';
                msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
                pathCard.querySelector('h3').appendChild(msg);
                setTimeout(() => msg.remove(), 1500);
            }
        });
    }

    if (browseSavePathBtn) {
        browseSavePathBtn.addEventListener('click', () => {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({ type: 'select_destination' }));
            } else {
                showToast('⚠️ השרת אינו מחובר כעת. מפעיל את התוכנה...');
                startServer();
                let checkTimer = setInterval(() => {
                    if (ws && ws.readyState === WebSocket.OPEN) {
                        clearInterval(checkTimer);
                        ws.send(JSON.stringify({ type: 'select_destination' }));
                    }
                }, 500);
                setTimeout(() => clearInterval(checkTimer), 6000);
            }
        });
    }

    if (resetPathBtn) {
        resetPathBtn.addEventListener('click', () => {
            if (savePathInput) savePathInput.value = '';
            chrome.storage.local.remove(['customSavePath']);
            // Show inline message in settings card
            const pathCard = document.querySelector('.settings-card:nth-child(1)'); // Save path card
            if (pathCard) {
                const msg = document.createElement('span');
                msg.textContent = '📁 שוחזר';
                msg.style.cssText = 'color: #10b981; font-size: 12px; margin-inline-start: 8px;';
                pathCard.querySelector('h3').appendChild(msg);
                setTimeout(() => msg.remove(), 1500);
            }
        });
    }

    // Server start triggers
    startServerBtn.addEventListener('click', startServer);
    serverStatusPill.addEventListener('click', () => {
        if (!ws || ws.readyState !== WebSocket.OPEN) startServer();
    });

    // Global Click Delegation for Settings Toggle & Action Buttons
    document.addEventListener('click', (e) => {
        const targetBtn = e.target.closest('#settings-btn');
        if (targetBtn) {
            e.preventDefault();
            e.stopPropagation();

            const sView = document.getElementById('settings-view');
            const mView = document.getElementById('main-view');
            const offBanner = document.getElementById('server-offline-banner');

            if (!sView || !mView) return;

            const isSettingsOpen = !sView.classList.contains('hidden');
            if (isSettingsOpen) {
                // Close settings -> Go back to main
                sView.classList.add('hidden');
                mView.classList.remove('hidden');
                targetBtn.classList.remove('active');
                if (offBanner && (!ws || ws.readyState !== WebSocket.OPEN)) {
                    offBanner.classList.remove('hidden');
                }
                updateQualityBadge();
            } else {
                // Open settings
                mView.classList.add('hidden');
                sView.classList.remove('hidden');
                targetBtn.classList.add('active');
                // Hide offline banner in settings view
                if (offBanner) offBanner.classList.add('hidden');
            }
        }
    });

    // Quality Indicator & Modal Triggers
    if (currentQualityBadge) {
        currentQualityBadge.addEventListener('click', (e) => {
            e.stopPropagation();
            if (qualityModalOverlay && !qualityModalOverlay.classList.contains('hidden')) {
                closeQualityModal();
            } else {
                openQualityModal();
            }
        });
    }

    if (closeQualityModalBtn) {
        closeQualityModalBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            closeQualityModal();
        });
    }

    if (qualityModalOverlay) {
        qualityModalOverlay.addEventListener('click', (e) => {
            if (e.target === qualityModalOverlay) {
                closeQualityModal();
            }
        });
    }

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeQualityModal();
        }
    });

    // Modal Format Switcher Tabs
    if (modalTabMp3) {
        modalTabMp3.addEventListener('click', () => {
            selectFormatAndQuality('mp3', selectedMp3Quality, false);
        });
    }

    if (modalTabMp4) {
        modalTabMp4.addEventListener('click', () => {
            selectFormatAndQuality('mp4', selectedMp4Quality, false);
        });
    }

    // Modal Quality Option Buttons
    document.querySelectorAll('.quality-option-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const type = btn.getAttribute('data-type');
            const format = btn.getAttribute('data-format');
            selectFormatAndQuality(type, format, true);
            setTimeout(closeQualityModal, 220);
        });
    });

    // Format Switcher (Above URL)
    tabMp3.addEventListener('click', () => {
        selectFormatAndQuality('mp3', selectedMp3Quality, false);
    });

    tabMp4.addEventListener('click', () => {
        selectFormatAndQuality('mp4', selectedMp4Quality, false);
    });

    // Quality selection in settings
    document.querySelectorAll('#mp3-qualities .chip, #mp4-qualities .chip').forEach(chip => {
        chip.addEventListener('click', (e) => {
            const type = e.target.getAttribute('data-type');
            const format = e.target.getAttribute('data-format');
            selectFormatAndQuality(type, format, true);
        });
    });

    // Paste / Clear
    pasteBtn.addEventListener('click', async () => {
        try {
            const text = await navigator.clipboard.readText();
            if (text) {
                // Auto-add to playlist
                addVideoToPlaylist(
                    text,
                    detectedMedia.url === text ? detectedMedia.title : null,
                    detectedMedia.url === text ? detectedMedia.thumbnail : null,
                    detectedMedia.url === text ? (text.includes('youtube.com') || text.includes('youtu.be') ? 'YouTube' : 'Netube') : 'Unknown',
                    detectedMedia.url === text ? (detectedMedia.channel || '') : ''
                );
                // Clear input automatically
                urlInput.value = '';
                clearBtn.classList.add('hidden');
                mediaPreview.classList.add('hidden');
            }
        } catch (e) {
            urlInput.focus();
        }
    });

    clearBtn.addEventListener('click', () => {
        urlInput.value = '';
        clearBtn.classList.add('hidden');
        // Don't hide media preview - keep it visible
    });

    addBtn.addEventListener('click', () => {
        triggerDownload();
    });

    urlInput.addEventListener('input', () => {
        if (urlInput.value.trim().length > 0) {
            clearBtn.classList.remove('hidden');
        } else {
            clearBtn.classList.add('hidden');
            // Don't hide media preview on input change
        }
    });

    // Handle Enter key in URL input
    urlInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            triggerDownload();
        }
    });

    // Download All Button
    downloadAllBtn.addEventListener('click', downloadAllFromPlaylist);

    // Remove All Button
    removeAllBtn.addEventListener('click', clearPlaylist);

    // Cancel All Downloads Button
    if (cancelAllDownloadsBtn) {
        cancelAllDownloadsBtn.addEventListener('click', cancelAllDownloads);
    }

    // Toggle All Pause Button (Pause All / Resume All)
    if (toggleAllPauseBtn) {
        toggleAllPauseBtn.addEventListener('click', toggleAllDownloadsPause);
    }

    // Mode Switcher (Link vs Artist Search)
    if (modeLinkBtn && modeSearchBtn) {
        modeLinkBtn.addEventListener('click', () => {
            modeLinkBtn.classList.add('active');
            modeSearchBtn.classList.remove('active');
            linkInputSection.classList.remove('hidden');
            artistSearchSection.classList.add('hidden');
        });

        modeSearchBtn.addEventListener('click', () => {
            modeSearchBtn.classList.add('active');
            modeLinkBtn.classList.remove('active');
            linkInputSection.classList.add('hidden');
            artistSearchSection.classList.remove('hidden');
            artistQueryInput.focus();
        });
    }

    // Search Count Chips
    countChips.forEach(chip => {
        chip.addEventListener('click', () => {
            countChips.forEach(c => c.classList.remove('active'));
            chip.classList.add('active');
            const dataVal = chip.getAttribute('data-count');
            if (dataVal === 'all') {
                selectedSearchCount = 'all';
            } else {
                selectedSearchCount = parseInt(dataVal, 10) || 10;
            }
            if (customCountInput) customCountInput.value = '';
        });
    });

    // Custom Count Manual Input
    if (customCountInput) {
        customCountInput.addEventListener('input', () => {
            const val = parseInt(customCountInput.value, 10);
            if (!isNaN(val) && val > 0) {
                selectedSearchCount = Math.min(Math.max(val, 1), 100);
                countChips.forEach(c => c.classList.remove('active'));
            }
        });
    }

    // Artist Query Input & Clear Button
    if (artistQueryInput && artistClearBtn) {
        artistQueryInput.addEventListener('input', () => {
            if (artistQueryInput.value.trim().length > 0) {
                artistClearBtn.classList.remove('hidden');
            } else {
                artistClearBtn.classList.add('hidden');
            }
        });

        artistClearBtn.addEventListener('click', () => {
            artistQueryInput.value = '';
            artistClearBtn.classList.add('hidden');
            artistQueryInput.focus();
        });
    }

    // Search Trigger (Click & Enter)
    const doArtistSearch = () => {
        const query = artistQueryInput.value.trim();
        if (!query) {
            // Show inline error in search input
            artistQueryInput.style.borderColor = '#ef4444';
            artistQueryInput.placeholder = '⚠️ נא להזין שם אמן או מילות חיפוש';
            artistQueryInput.focus();
            setTimeout(() => {
                artistQueryInput.style.borderColor = '';
                artistQueryInput.placeholder = 'הקלד שם אמן או שיר...';
            }, 2000);
            return;
        }
        searchYouTubeArtist(query, selectedSearchCount);
    };

    if (artistSearchBtn) {
        artistSearchBtn.addEventListener('click', doArtistSearch);
    }
    if (artistQueryInput) {
        artistQueryInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                doArtistSearch();
            }
        });
    }

    // Select / Deselect All Results
    if (selectAllResultsCb) {
        selectAllResultsCb.addEventListener('change', () => {
            const isChecked = selectAllResultsCb.checked;
            searchResults.forEach(item => item.selected = isChecked);
            document.querySelectorAll('.search-item').forEach(row => {
                const cb = row.querySelector('input[type="checkbox"]');
                if (cb) cb.checked = isChecked;
                row.classList.toggle('selected', isChecked);
            });
            updateSearchResultsHeader();
        });
    }

    // Add Selected Results to Playlist
    if (addSelectedBtn) {
        addSelectedBtn.addEventListener('click', addSelectedSearchResultsToPlaylist);
    }

    // Extension Settings storage
    genericBtnToggle.addEventListener('change', (e) => {
        chrome.storage.local.get(['userConfig'], (res) => {
            let uConf = res.userConfig || {};
            uConf.enableGenericFloatingBtn = e.target.checked;
            chrome.storage.local.set({ userConfig: uConf });
        });
    });

    // Stop Server Button Handler
    if (stopServerSettingsBtn) {
        stopServerSettingsBtn.addEventListener('click', stopServer);
    }
});

