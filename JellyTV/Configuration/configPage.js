const PLUGIN_ID = '6e2f3159-1f9e-4972-b70c-8a076905f2b3';

// DOM helpers
const $ = sel => document.querySelector(sel);
const $$ = sel => document.querySelectorAll(sel);

// API helpers
const buildApiUrl = path =>
    ApiClient.getApiUrl?.(path) ?? ApiClient.getUrl?.(path) ?? ApiClient._getApiUrl?.(path) ?? path;

const appendHeaders = (headers, source) => {
    if (!source) return;
    if (source instanceof Headers) {
        source.forEach((value, key) => { headers[key] = value; });
        return;
    }
    Object.assign(headers, source);
};

const readApiClientValue = value => {
    try {
        return typeof value === 'function' ? value.call(ApiClient) : value;
    } catch {
        return null;
    }
};

const getAccessToken = () => {
    const candidates = [
        ApiClient.getAccessToken,
        ApiClient.accessToken,
        ApiClient._accessToken,
        ApiClient.token,
        ApiClient._token,
        ApiClient.serverInfo,
        ApiClient._serverInfo,
        ApiClient.credentials,
        ApiClient._credentials
    ];

    for (const candidate of candidates) {
        const value = readApiClientValue(candidate);
        if (!value) continue;
        if (typeof value === 'string') return value;
        if (typeof value.AccessToken === 'string') return value.AccessToken;
        if (typeof value.accessToken === 'string') return value.accessToken;
    }

    return null;
};

const getHeaders = () => {
    const headers = {};
    appendHeaders(headers, readApiClientValue(ApiClient.getFetchHeaders));
    const token = getAccessToken();
    if (token) {
        headers.Authorization ??= `MediaBrowser Token="${token}"`;
        headers['X-Emby-Token'] ??= token;
        headers['X-MediaBrowser-Token'] ??= token;
    }
    headers['Content-Type'] = 'application/json';
    return headers;
};

const apiPost = (path, body) =>
    fetch(buildApiUrl(path), { method: 'POST', headers: getHeaders(), body: JSON.stringify(body) });

const apiGet = path =>
    ApiClient.getJSON?.(buildApiUrl(path)) ?? fetch(buildApiUrl(path), { headers: getHeaders() }).then(r => r.json());

// UI helpers
const normalizeId = id => id ? id.toString().toLowerCase().replace(/[^a-f0-9]/g, '') : '';
const getInitials = name => (name || '?').charAt(0).toUpperCase();
const getPrefClass = v => v === true ? 'pref-on' : v === false ? 'pref-off' : 'pref-default';
const getPrefLabel = v => v === true ? 'On' : v === false ? 'Off' : 'Default';
const normalizeBannerIconType = value => ['info', 'warning', 'alert', 'success'].includes(value) ? value : 'info';
const updateLanguageOverrideState = () => {
    const override = $('#OverrideServerLanguage').checked;
    $('#PreferredLanguage').disabled = !override;
};
const updateBannerState = () => {
    const enabled = $('#BannerEnabled').checked;
    const expirationEnabled = enabled && $('#BannerExpirationEnabled').checked;
    $('#BannerMessageContainer').style.display = enabled ? '' : 'none';
    $('#BannerIconTypeContainer').style.display = enabled ? '' : 'none';
    $('#BannerExpirationToggleContainer').style.display = enabled ? '' : 'none';
    $('#BannerExpiresAtContainer').style.display = expirationEnabled ? '' : 'none';
    $('#BannerMessage').disabled = !enabled;
    $('#BannerIconType').disabled = !enabled;
    $('#BannerExpirationEnabled').disabled = !enabled;
    $('#BannerExpiresAt').disabled = !expirationEnabled;
};

const utcToLocalDateTimeInput = value => {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    const local = new Date(date.getTime() - (date.getTimezoneOffset() * 60000));
    return local.toISOString().slice(0, 16);
};

const localDateTimeInputToUtc = value => {
    if (!value) return null;
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return null;
    return date.toISOString().replace(/\.\d{3}Z$/, 'Z');
};

const makeSecret = () => {
    const bytes = new Uint8Array(24);
    if (window.crypto?.getRandomValues) {
        window.crypto.getRandomValues(bytes);
    } else {
        for (let i = 0; i < bytes.length; i++) {
            bytes[i] = Math.floor(Math.random() * 256);
        }
    }

    return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
};

const updateArrWebhookUrl = () => {
    const secret = $('#ArrWebhookSecret').value.trim();
    const baseUrl = buildApiUrl('JellyTV/notifications');
    $('#ArrWebhookUrl').value = secret ? `${baseUrl}?token=${encodeURIComponent(secret)}` : baseUrl;
};

const showStatus = (sel, type, msg) => {
    const el = $(sel);
    el.className = `jellytv-status ${type}`;
    el.style.display = 'block';
    el.textContent = msg;
};

const hideStatus = sel => { $(sel).style.display = 'none'; };

const showConfirmDialog = (title, message) => new Promise(resolve => {
    const overlay = document.createElement('div');
    overlay.className = 'jellytv-modal-overlay';
    overlay.innerHTML = `<div class="jellytv-modal">
        <div class="jellytv-modal-title">${title}</div>
        <div class="jellytv-modal-body">${message}</div>
        <div class="jellytv-modal-actions">
            <button class="jellytv-modal-btn jellytv-modal-btn-cancel">Cancel</button>
            <button class="jellytv-modal-btn jellytv-modal-btn-delete">Delete</button>
        </div>
    </div>`;
    document.body.appendChild(overlay);
    const close = result => { overlay.remove(); resolve(result); };
    overlay.querySelector('.jellytv-modal-btn-cancel').onclick = () => close(false);
    overlay.querySelector('.jellytv-modal-btn-delete').onclick = () => close(true);
    overlay.onclick = e => e.target === overlay && close(false);
});

// Tab switching
$$('.jellytv-tab').forEach(tab => {
    tab.addEventListener('click', function() {
        const targetTab = this.getAttribute('data-tab');
        $$('.jellytv-tab').forEach(t => t.classList.remove('active'));
        this.classList.add('active');
        $$('.jellytv-tab-content').forEach(c => c.classList.remove('active'));
        $(`#tab-${targetTab}`).classList.add('active');
        if (targetTab === 'users') renderRegisteredUsers();
    });
});

// Character counters
const attachCounter = (textareaSelector, countSelector, containerSelector) => {
    const textarea = $(textareaSelector);
    const counter = $(countSelector);
    const container = $(containerSelector);
    const maxLength = 4000;

    const updateCounter = () => {
        const len = textarea.value.length;
        counter.textContent = len;
        container.classList.remove('warning', 'error');
        if (len >= maxLength) container.classList.add('error');
        else if (len >= maxLength * 0.9) container.classList.add('warning');
    };

    textarea.addEventListener('input', updateCounter);
    updateCounter();
};

attachCounter('#BroadcastMessage', '#BroadcastCharCount', '#BroadcastCharCounter');
attachCounter('#BannerMessage', '#BannerCharCount', '#BannerCharCounter');

// Configuration loading
const loadConfiguration = async () => {
    Dashboard.showLoadingMsg();
    try {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        $('#SeerrBaseUrl').value = config.SeerrBaseUrl || '';
        $('#BannerEnabled').checked = config.BannerEnabled === true;
        $('#BannerIconType').value = normalizeBannerIconType(config.BannerIconType);
        $('#BannerMessage').value = config.BannerMessage || '';
        $('#BannerExpiresAt').value = utcToLocalDateTimeInput(config.BannerExpiresAtUtc);
        $('#BannerExpirationEnabled').checked = !!config.BannerExpiresAtUtc;
        $('#BannerCharCount').textContent = ($('#BannerMessage').value || '').length;
        updateBannerState();
        $('#ArrWebhookSecret').value = config.ArrWebhookSecret || makeSecret();
        updateArrWebhookUrl();
        $('#OverrideServerLanguage').checked = config.OverrideServerLanguage === true;
        $('#PreferredLanguage').value = config.PreferredLanguage || 'en';
        updateLanguageOverrideState();
        $('#ForwardItemAdded').checked = config.ForwardItemAdded === true;

        let playbackStart = config.ForwardPlaybackStart === true;
        let playbackStop = config.ForwardPlaybackStop === true;
        // Backward compatibility
        if (typeof config.ForwardPlayback === 'boolean' && !config.ForwardPlaybackStart && !config.ForwardPlaybackStop) {
            playbackStart = playbackStop = config.ForwardPlayback;
        }
        $('#ForwardPlaybackStart').checked = playbackStart;
        $('#ForwardPlaybackStop').checked = playbackStop;

        await renderRegisteredUsers();
    } catch { /* ignore */ }
    finally { Dashboard.hideLoadingMsg(); }
};

// User management
const deleteUser = async (userId, displayName) => {
    const confirmed = await showConfirmDialog(
        'Remove User Registration',
        `Are you sure you want to remove <strong>${displayName}</strong> from push notifications? They will need to re-register their device in the JellyTV app to receive notifications again.`
    );
    if (!confirmed) return;

    Dashboard.showLoadingMsg();
    try {
        const res = await apiPost(`Plugins/${PLUGIN_ID}/JellyTV/users/delete`, { userId });
        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            throw new Error(data.error || `Failed to delete user (status ${res.status})`);
        }
        await renderRegisteredUsers();
    } catch (err) {
        alert(err.message || 'Failed to delete user.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
};

const renderRegisteredUsers = async () => {
    const list = $('#RegisteredUsersList');
    list.innerHTML = '<li style="justify-content: center; background: transparent;">Loading...</li>';

    try {
        const entries = await apiGet(`Plugins/${PLUGIN_ID}/JellyTV/users`) || [];
        list.innerHTML = '';

        if (!entries.length) {
            list.innerHTML = `<div class="jellytv-empty-state">
                <div class="jellytv-empty-state-icon">&#128274;</div>
                <div>No registered users yet</div>
                <div style="font-size: 12px; margin-top: 8px;">Users will appear here once they register their devices in the JellyTV app.</div>
            </div>`;
            return;
        }

        const users = await ApiClient.getUsers();
        const userMap = Object.fromEntries(
            (users || []).map(u => [normalizeId(u.Id), { name: u.Name || u.Username || '', id: u.Id, hasImage: !!u.PrimaryImageTag }])
        );

        const prefs = await Promise.all(
            entries.map(u => apiGet(`Plugins/${PLUGIN_ID}/JellyTV/preferences/${u.userId || u.UserId}`).catch(() => null))
        );

        entries.forEach((u, i) => {
            const uid = u.userId || u.UserId || '';
            const userData = userMap[normalizeId(uid)];
            const isDeleted = !userData;
            const name = isDeleted ? '(deleted user)' : userData.name;
            const userPrefs = prefs[i] || {};

            const li = document.createElement('li');
            if (isDeleted) li.style.opacity = '0.6';

            const imageUrl = !isDeleted && userData.hasImage
                ? `${buildApiUrl(`Users/${userData.id}/Images/Primary`)}?height=80&quality=90`
                : null;

            const iconHtml = imageUrl
                ? `<img class="jellytv-user-avatar" src="${imageUrl}" alt="${name}" onerror="this.outerHTML='<span class=jellytv-user-icon>${getInitials(name)}</span>'">`
                : `<span class="jellytv-user-icon">${getInitials(name)}</span>`;

            const prefHtml = `<div class="jellytv-user-prefs">
                <span class="jellytv-pref-tag ${getPrefClass(userPrefs.ForwardItemAdded)}" title="${getPrefLabel(userPrefs.ForwardItemAdded)}">Item added</span>
                <span class="jellytv-pref-tag ${getPrefClass(userPrefs.ForwardPlaybackStart)}" title="${getPrefLabel(userPrefs.ForwardPlaybackStart)}">Playback start</span>
                <span class="jellytv-pref-tag ${getPrefClass(userPrefs.ForwardPlaybackStop)}" title="${getPrefLabel(userPrefs.ForwardPlaybackStop)}">Playback stop</span>
            </div>`;

            const userInfo = document.createElement('div');
            userInfo.className = 'jellytv-user-info';
            userInfo.innerHTML = `${iconHtml}<span class="jellytv-user-name">${name}</span>${prefHtml}`;

            const deleteBtn = document.createElement('button');
            deleteBtn.className = 'jellytv-delete-btn';
            deleteBtn.textContent = 'Remove';
            deleteBtn.onclick = () => deleteUser(uid, name);

            li.append(userInfo, deleteBtn);
            list.appendChild(li);
        });
    } catch (err) {
        console.error('Failed to load registered users:', err);
        list.innerHTML = '<li style="color: #f44336;">Failed to load registered users.</li>';
    }
};

// Form submission
$('#TemplateConfigForm').addEventListener('submit', async e => {
    e.preventDefault();
    Dashboard.showLoadingMsg();
    try {
        const arrWebhookSecret = $('#ArrWebhookSecret').value.trim();
        hideStatus('#ArrWebhookStatus');
        if (!arrWebhookSecret) {
            showStatus('#ArrWebhookStatus', 'error', 'A URL token is required for Arr webhooks.');
            return;
        }

        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.SeerrBaseUrl = $('#SeerrBaseUrl').value.trim();
        config.ArrWebhookSecret = arrWebhookSecret;
        config.OverrideServerLanguage = $('#OverrideServerLanguage').checked;
        config.PreferredLanguage = $('#PreferredLanguage').value || 'en';
        config.ForwardItemAdded = $('#ForwardItemAdded').checked;
        config.ForwardPlaybackStart = $('#ForwardPlaybackStart').checked;
        config.ForwardPlaybackStop = $('#ForwardPlaybackStop').checked;
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        Dashboard.processPluginConfigurationUpdateResult(result);
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

// Broadcast notification
$('#OverrideServerLanguage').addEventListener('change', updateLanguageOverrideState);
$('#BannerEnabled').addEventListener('change', updateBannerState);
$('#BannerExpirationEnabled').addEventListener('change', updateBannerState);

$('#SaveBannerBtn').addEventListener('click', async e => {
    e.preventDefault();
    Dashboard.showLoadingMsg();
    hideStatus('#BannerStatus');

    try {
        const enabled = $('#BannerEnabled').checked;
        const message = $('#BannerMessage').value.trim();
        if (enabled && !message) {
            showStatus('#BannerStatus', 'error', 'A message is required when the banner is enabled.');
            return;
        }

        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.BannerEnabled = enabled;
        config.BannerIconType = enabled ? normalizeBannerIconType($('#BannerIconType').value) : 'info';
        config.BannerMessage = enabled ? message : '';
        config.BannerExpiresAtUtc = enabled && $('#BannerExpirationEnabled').checked
            ? localDateTimeInputToUtc($('#BannerExpiresAt').value)
            : null;
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        showStatus('#BannerStatus', 'success', 'Banner saved successfully!');
        Dashboard.processPluginConfigurationUpdateResult(result);
    } catch (err) {
        showStatus('#BannerStatus', 'error', err.message || 'Failed to save banner.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

$('#ClearBannerBtn').addEventListener('click', async e => {
    e.preventDefault();
    Dashboard.showLoadingMsg();
    hideStatus('#BannerStatus');

    try {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.BannerEnabled = false;
        config.BannerIconType = 'info';
        config.BannerMessage = '';
        config.BannerExpiresAtUtc = null;
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        $('#BannerEnabled').checked = false;
        $('#BannerIconType').value = 'info';
        $('#BannerExpirationEnabled').checked = false;
        $('#BannerMessage').value = '';
        $('#BannerExpiresAt').value = '';
        $('#BannerCharCount').textContent = '0';
        updateBannerState();
        showStatus('#BannerStatus', 'success', 'Banner cleared successfully!');
        Dashboard.processPluginConfigurationUpdateResult(result);
    } catch (err) {
        showStatus('#BannerStatus', 'error', err.message || 'Failed to clear banner.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

$('#SendBroadcastBtn').addEventListener('click', async e => {
    e.preventDefault();
    const message = $('#BroadcastMessage').value.trim();

    if (!message) {
        showStatus('#BroadcastStatus', 'error', 'Please enter a message.');
        return;
    }

    Dashboard.showLoadingMsg();
    hideStatus('#BroadcastStatus');

    try {
        const res = await apiPost(`Plugins/${PLUGIN_ID}/JellyTV/broadcast`, { message });
        if (res.status === 429) throw new Error('Rate limited. Please wait before sending another notification.');
        if (!res.ok) {
            const data = await res.json();
            throw new Error(data.error || 'Failed to send notification');
        }
        showStatus('#BroadcastStatus', 'success', 'Notification sent successfully!');
        $('#BroadcastMessage').value = '';
        $('#BroadcastCharCount').textContent = '0';
        $('#BroadcastCharCounter').classList.remove('warning', 'error');
    } catch (err) {
        showStatus('#BroadcastStatus', 'error', err.message || 'Failed to send notification.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

// Save Seerr URL
$('#SaveSeerrBtn').addEventListener('click', async e => {
    e.preventDefault();
    Dashboard.showLoadingMsg();
    hideStatus('#SeerrStatus');

    try {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.SeerrBaseUrl = $('#SeerrBaseUrl').value.trim();
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        showStatus('#SeerrStatus', 'success', 'Seerr URL saved successfully!');
        Dashboard.processPluginConfigurationUpdateResult(result);
    } catch (err) {
        showStatus('#SeerrStatus', 'error', err.message || 'Failed to save.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

// Save Arr webhook settings
$('#ArrWebhookSecret').addEventListener('input', updateArrWebhookUrl);

$('#GenerateArrWebhookSecretBtn').addEventListener('click', e => {
    e.preventDefault();
    $('#ArrWebhookSecret').value = makeSecret();
    updateArrWebhookUrl();
    showStatus('#ArrWebhookStatus', 'success', 'New URL token generated. Save to apply it.');
});

// Initialize
loadConfiguration();
$('#TemplateConfigPage').addEventListener('pageshow', loadConfiguration);
