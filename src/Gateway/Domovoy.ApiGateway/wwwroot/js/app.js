/**
 * Domovoy Smart Home — Web UI Dashboard
 * SignalR client for real-time device events
 */

// ── State ──────────────────────────────────
const state = {
    devices: new Map(),           // deviceId → device info
    notificationCount: 0,
    logEntries: 0,
    maxLogEntries: 200,
    maxNotifications: 50
};

// ── DOM Elements ───────────────────────────
const els = {
    connectionStatus: document.getElementById('connectionStatus'),
    connectionText: document.getElementById('connectionText'),
    notificationsList: document.getElementById('notificationsList'),
    notificationsEmpty: document.getElementById('notificationsEmpty'),
    notificationCount: document.getElementById('notificationCount'),
    devicesGrid: document.getElementById('devicesGrid'),
    devicesEmpty: document.getElementById('devicesEmpty'),
    deviceCount: document.getElementById('deviceCount'),
    eventLog: document.getElementById('eventLog'),
    logEmpty: document.getElementById('logEmpty')
};

// ── Helpers ─────────────────────────────────
function formatTime(date) {
    return date.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function formatTimeShort(date) {
    return date.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
}

function shortId(id) {
    if (!id) return '—';
    const s = String(id);
    if (s.length > 12) return s.substring(0, 8) + '…';
    return s;
}

// ── SignalR Connection ──────────────────────
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub/devices')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

// ── Connection Lifecycle ────────────────────
function setConnectionState(state) {
    els.connectionStatus.className = 'connection-status ' + state;
    const labels = {
        connected: 'Подключено',
        disconnected: 'Отключено',
        connecting: 'Подключение…'
    };
    els.connectionText.textContent = labels[state] || state;
}

connection.onreconnecting(() => {
    setConnectionState('connecting');
    addLogEntry('system', 'Переподключение к серверу…');
});

connection.onreconnected(() => {
    setConnectionState('connected');
    addLogEntry('success', 'Соединение восстановлено');
});

connection.onclose(() => {
    setConnectionState('disconnected');
    addLogEntry('error', 'Соединение потеряно');
});

async function startConnection() {
    setConnectionState('connecting');
    addLogEntry('system', 'Подключение к шлюзу…');

    try {
        await connection.start();
        setConnectionState('connected');
        addLogEntry('success', 'Подключено к Domovoy Gateway');
    } catch (err) {
        setConnectionState('disconnected');
        addLogEntry('error', `Ошибка подключения: ${err.message}`);
        console.error('SignalR connection error:', err);
        // retry in 5 seconds
        setTimeout(startConnection, 5000);
    }
}

// ── Event Handlers ──────────────────────────

/**
 * Handle DeviceDiscovered event from SignalR
 * Sent by EventRelayService.HandleDeviceDiscovered
 * Args: deviceId (Guid), deviceType (string), name (string)
 */
connection.on('DeviceDiscovered', (deviceId, deviceType, name) => {
    console.log('DeviceDiscovered:', { deviceId, deviceType, name });

    const now = new Date();
    const idStr = String(deviceId);

    // Update or create device in state
    const existing = state.devices.get(idStr);
    if (existing) {
        existing.name = name || existing.name;
        existing.deviceType = deviceType || existing.deviceType;
        existing.lastSeen = now;
        existing.status = 'discovered';
        updateDeviceCard(idStr);
    } else {
        state.devices.set(idStr, {
            id: idStr,
            name: name || idStr,
            deviceType: deviceType || 'Unknown',
            status: 'discovered',
            discoveredAt: now,
            lastSeen: now,
            state: {}
        });
        createDeviceCard(idStr);
    }

    // Notification
    addNotification(
        'new',
        '🆕 Новое устройство обнаружено',
        `${name || shortId(idStr)} (тип: ${deviceType || 'неизвестен'})`,
        now
    );

    // Log
    addLogEntry('discovery', `Обнаружено устройство: ${name || shortId(idStr)} [${deviceType}]`);

    updateCounters();
});

/**
 * Handle DeviceStateUpdated event from SignalR
 * Sent by EventRelayService.HandleDeviceStateChanged
 * Args: deviceId (string), state (object)
 */
connection.on('DeviceStateUpdated', (deviceId, deviceState) => {
    console.log('DeviceStateUpdated:', { deviceId, deviceState });

    const now = new Date();
    const idStr = String(deviceId);

    const existing = state.devices.get(idStr);
    if (existing) {
        existing.state = { ...existing.state, ...deviceState };
        existing.lastSeen = now;
        if (existing.status === 'discovered') {
            existing.status = 'paired';
        }
        updateDeviceCard(idStr);
    } else {
        // Device we haven't seen via discovery — create anyway
        state.devices.set(idStr, {
            id: idStr,
            name: idStr,
            deviceType: 'Unknown',
            status: 'paired',
            discoveredAt: now,
            lastSeen: now,
            state: deviceState || {}
        });
        createDeviceCard(idStr);
        updateCounters();
    }

    // Format state for log
    const stateStr = deviceState
        ? Object.entries(deviceState).map(([k, v]) => `${k}=${v}`).join(', ')
        : '(empty)';

    addLogEntry('state', `Состояние ${shortId(idStr)}: ${stateStr}`);
});

// ── Notifications ───────────────────────────
function addNotification(type, title, message, time) {
    // Hide empty state
    els.notificationsEmpty.style.display = 'none';

    // Limit notifications
    const children = els.notificationsList.querySelectorAll('.notification');
    if (children.length >= state.maxNotifications) {
        els.notificationsList.removeChild(children[children.length - 1]);
    }

    const el = document.createElement('div');
    el.className = `notification ${type}`;

    const icons = { new: '🔵', success: '✅', warning: '⚠️', error: '❌' };

    el.innerHTML = `
        <div class="notification-icon">${icons[type] || '📌'}</div>
        <div class="notification-content">
            <div class="notification-title">${escapeHtml(title)}</div>
            <div class="notification-message">${escapeHtml(message)}</div>
        </div>
        <span class="notification-time">${formatTimeShort(time || new Date())}</span>
    `;

    els.notificationsList.insertBefore(el, els.notificationsEmpty.nextSibling);
    state.notificationCount++;
    els.notificationCount.textContent = state.notificationCount;
    els.notificationCount.style.display = 'inline-flex';
}

function clearNotifications() {
    const notifs = els.notificationsList.querySelectorAll('.notification');
    notifs.forEach(n => n.remove());
    els.notificationsEmpty.style.display = 'flex';
    state.notificationCount = 0;
    els.notificationCount.style.display = 'none';
}

// ── Device Cards ────────────────────────────
function createDeviceCard(deviceId) {
    // Hide empty state
    els.devicesEmpty.style.display = 'none';

    const device = state.devices.get(deviceId);
    if (!device) return;

    const card = document.createElement('div');
    card.className = 'device-card';
    card.id = `device-${deviceId}`;

    renderDeviceCardContent(card, device);
    els.devicesGrid.insertBefore(card, els.devicesGrid.firstChild);
}

function updateDeviceCard(deviceId) {
    const device = state.devices.get(deviceId);
    if (!device) return;

    let card = document.getElementById(`device-${deviceId}`);
    if (!card) {
        createDeviceCard(deviceId);
        return;
    }

    renderDeviceCardContent(card, device);
}

function renderDeviceCardContent(card, device) {
    const statusLabels = {
        discovered: 'Обнаружено',
        interviewing: 'Опрос…',
        paired: 'Сопряжено',
        error: 'Ошибка'
    };

    let stateHtml = '';
    if (device.state && Object.keys(device.state).length > 0) {
        const stateItems = Object.entries(device.state)
            .filter(([k]) => !['linkquality'].includes(k))
            .slice(0, 6)
            .map(([key, value]) => {
                const displayValue = typeof value === 'number'
                    ? (Number.isInteger(value) ? value : value.toFixed(1))
                    : value;
                const unitMap = {
                    temperature: '°C',
                    humidity: '%',
                    battery: '%',
                    linkquality: 'lqi'
                };
                const unit = unitMap[key] || '';
                const labelMap = {
                    temperature: 'Темп.',
                    humidity: 'Влаж.',
                    battery: 'Батарея',
                    temperature_unit: 'Ед.',
                    temperature_calibration: 'Калибр.'
                };
                return `
                    <div class="state-item">
                        <div class="state-value">${escapeHtml(String(displayValue))}${unit ? '<small>' + unit + '</small>' : ''}</div>
                        <div class="state-label">${escapeHtml(labelMap[key] || key)}</div>
                    </div>
                `;
            }).join('');

        if (stateItems) {
            stateHtml = `<div class="device-state-grid">${stateItems}</div>`;
        }
    }

    card.innerHTML = `
        <div class="device-card-header">
            <div class="device-name" title="${escapeHtml(device.id)}">${escapeHtml(device.name)}</div>
            <div class="device-status ${device.status}">
                <span class="device-status-dot"></span>
                ${statusLabels[device.status] || device.status}
            </div>
        </div>
        <div class="device-info">
            <div class="device-info-row">
                <span class="device-info-label">Тип</span>
                <span class="device-info-value">${escapeHtml(device.deviceType)}</span>
            </div>
            <div class="device-info-row">
                <span class="device-info-label">ID</span>
                <span class="device-info-value" title="${escapeHtml(device.id)}">${escapeHtml(shortId(device.id))}</span>
            </div>
            <div class="device-info-row">
                <span class="device-info-label">Обнаружено</span>
                <span class="device-info-value">${formatTime(device.discoveredAt)}</span>
            </div>
        </div>
        ${stateHtml}
    `;
}

// ── Event Log ───────────────────────────────
function addLogEntry(type, message) {
    els.logEmpty.style.display = 'none';

    // Limit log entries
    const entries = els.eventLog.querySelectorAll('.log-entry');
    if (entries.length >= state.maxLogEntries) {
        els.eventLog.removeChild(entries[entries.length - 1]);
    }

    const el = document.createElement('div');
    el.className = 'log-entry';

    const typeLabels = {
        discovery: 'Обнаруж.',
        state: 'Состояние',
        interview: 'Опрос',
        success: 'Успешно',
        error: 'Ошибка',
        system: 'Система'
    };

    el.innerHTML = `
        <span class="log-time">${formatTime(new Date())}</span>
        <span class="log-type ${type}">${typeLabels[type] || type}</span>
        <span class="log-message">${escapeHtml(message)}</span>
    `;

    // Insert after logEmpty, before other entries
    const firstEntry = els.eventLog.querySelector('.log-entry');
    if (firstEntry) {
        els.eventLog.insertBefore(el, firstEntry);
    } else {
        els.eventLog.appendChild(el);
    }

    state.logEntries++;
}

function clearLog() {
    const entries = els.eventLog.querySelectorAll('.log-entry');
    entries.forEach(e => e.remove());
    els.logEmpty.style.display = 'flex';
    state.logEntries = 0;
}

// ── Counters ────────────────────────────────
function updateCounters() {
    const count = state.devices.size;
    els.deviceCount.textContent = count;
    els.deviceCount.style.display = count > 0 ? 'inline-flex' : 'none';
}

// ── Utils ───────────────────────────────────
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ── Init ────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    startConnection();
});
