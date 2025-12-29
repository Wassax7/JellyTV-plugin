const PLUGIN_ID = 'eb5d7894-8eef-4b36-aa6f-5d124e828ce1';

// DOM helpers
const $ = sel => document.querySelector(sel);
const $$ = sel => document.querySelectorAll(sel);

// API helpers
const buildApiUrl = path =>
    ApiClient.getApiUrl?.(path) ?? ApiClient.getUrl?.(path) ?? ApiClient._getApiUrl?.(path) ?? path;

const getHeaders = () => ({ ...(ApiClient.getFetchHeaders?.() ?? {}), 'Content-Type': 'application/json' });

const apiPost = (path, body) =>
    fetch(buildApiUrl(path), { method: 'POST', headers: getHeaders(), body: JSON.stringify(body) });

const apiGet = path =>
    ApiClient.getJSON?.(buildApiUrl(path)) ?? fetch(buildApiUrl(path), { headers: getHeaders() }).then(r => r.json());

// UI helpers
const normalizeId = id => id ? id.toString().toLowerCase().replace(/[^a-f0-9]/g, '') : '';
const getInitials = name => (name || '?').charAt(0).toUpperCase();
const getPrefClass = v => v === true ? 'pref-on' : v === false ? 'pref-off' : 'pref-default';
const getPrefLabel = v => v === true ? 'On' : v === false ? 'Off' : 'Default';

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

// Character counter
(() => {
    const textarea = $('#BroadcastMessage');
    const counter = $('#BroadcastCharCount');
    const container = $('#BroadcastCharCounter');
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
})();

// Configuration loading
const loadConfiguration = async () => {
    Dashboard.showLoadingMsg();
    try {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        $('#JellyseerrBaseUrl').value = config.JellyseerrBaseUrl || '';
        $('#PreferredLanguage').value = config.PreferredLanguage || 'en';
        $('#ForwardItemAdded').checked = config.ForwardItemAdded === true;

        let playbackStart = config.ForwardPlaybackStart === true;
        let playbackStop = config.ForwardPlaybackStop === true;
        // Backward compatibility
        if (typeof config.ForwardPlayback === 'boolean' && !config.ForwardPlaybackStart && !config.ForwardPlaybackStop) {
            playbackStart = playbackStop = config.ForwardPlayback;
        }
        $('#ForwardPlaybackStart').checked = playbackStart;
        $('#ForwardPlaybackStop').checked = playbackStop;
        $('#SendRegistrationConfirmation').checked = config.SendRegistrationConfirmation !== false;

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
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.JellyseerrBaseUrl = $('#JellyseerrBaseUrl').value.trim();
        config.PreferredLanguage = $('#PreferredLanguage').value || 'en';
        config.ForwardItemAdded = $('#ForwardItemAdded').checked;
        config.ForwardPlaybackStart = $('#ForwardPlaybackStart').checked;
        config.ForwardPlaybackStop = $('#ForwardPlaybackStop').checked;
        config.SendRegistrationConfirmation = $('#SendRegistrationConfirmation').checked;
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        Dashboard.processPluginConfigurationUpdateResult(result);
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

// Broadcast notification
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

// Save Jellyseerr URL
$('#SaveJellyseerrBtn').addEventListener('click', async e => {
    e.preventDefault();
    Dashboard.showLoadingMsg();
    hideStatus('#JellyseerrStatus');

    try {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.JellyseerrBaseUrl = $('#JellyseerrBaseUrl').value.trim();
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        showStatus('#JellyseerrStatus', 'success', 'Jellyseerr URL saved successfully!');
        Dashboard.processPluginConfigurationUpdateResult(result);
    } catch (err) {
        showStatus('#JellyseerrStatus', 'error', err.message || 'Failed to save.');
    } finally {
        Dashboard.hideLoadingMsg();
    }
});

// Initialize
loadConfiguration();
$('#TemplateConfigPage').addEventListener('pageshow', loadConfiguration);
