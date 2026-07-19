// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Connection manager (mobile-app / remote-access track, Epic 2O.3d). The launcher's whole job: know the home's
 * addresses and pick a reachable one, in priority order, before handing the WebView to the live UI.
 *
 *   1. LAN         — http://192.168.x.x        fast, no internet, at home
 *   2. IPv6-direct — https://home.<domain>     primary remote (AAAA + firewall pinhole), beats CGNAT
 *   3. SSH-fallback— https://home.<domain>     via the VPS reverse tunnel, when IPv6 can't reach
 *
 * We probe GET {origin}/health (anonymous, cheap — the ApiGateway maps it AllowAnonymous) with a short timeout;
 * the first origin that answers 200 wins and we navigate there. The UI itself is same-origin, so nothing in the
 * home UI needs to know which transport carried it.
 */

const STORE_KEY = 'domovoy.connection';
const PROBE_TIMEOUT_MS = 1500;
const HEALTH_PATH = '/health';

// --- storage (Capacitor Preferences when present, localStorage otherwise) ---------------------------------

const prefs = () => window.Capacitor?.Plugins?.Preferences ?? null;

async function loadConfig() {
  try {
    const p = prefs();
    if (p) {
      const { value } = await p.get({ key: STORE_KEY });
      return value ? JSON.parse(value) : null;
    }
    const raw = localStorage.getItem(STORE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

async function saveConfig(config) {
  const value = JSON.stringify(config);
  const p = prefs();
  if (p) await p.set({ key: STORE_KEY, value });
  else localStorage.setItem(STORE_KEY, value);
}

// --- probing ----------------------------------------------------------------------------------------------

const trimOrigin = (url) => url.trim().replace(/\/+$/, '');

/** True if {origin}/health answers 200 within the timeout. Any error/timeout → false (try the next origin). */
async function reachable(origin) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
  try {
    const res = await fetch(`${origin}${HEALTH_PATH}`, {
      method: 'GET',
      cache: 'no-store',
      signal: controller.signal,
    });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

/** Ordered, de-duplicated list of candidate origins from the saved config. */
function candidates(config) {
  return [config.lan, config.ipv6, config.ssh]
    .filter(Boolean)
    .map(trimOrigin)
    .filter((o, i, arr) => arr.indexOf(o) === i);
}

/** Probe candidates in priority order; navigate to the first reachable one. Returns false if none answer. */
async function connect(config) {
  for (const origin of candidates(config)) {
    if (await reachable(origin)) {
      // Remember which one worked so the next launch tries it first (cheap optimisation, not persisted order).
      window.location.replace(origin);
      return true;
    }
  }
  return false;
}

// --- UI ---------------------------------------------------------------------------------------------------

const $ = (id) => document.getElementById(id);
const show = (id) => $(id).classList.remove('hidden');
const hide = (id) => $(id).classList.add('hidden');

function showSetup(config, errorText) {
  hide('connecting');
  show('setup');
  if (config) {
    $('lan').value = config.lan ?? '';
    $('ipv6').value = config.ipv6 ?? '';
    $('ssh').value = config.ssh ?? '';
  }
  if (errorText) {
    $('setup-error').textContent = errorText;
    show('setup-error');
  } else {
    hide('setup-error');
  }
}

function showConnecting() {
  hide('setup');
  show('connecting');
}

async function attempt(config) {
  showConnecting();
  const ok = await connect(config);
  if (!ok) showSetup(config, 'Не удалось подключиться ни по одному адресу. Проверьте адреса и сеть.');
}

$('connect').addEventListener('click', async () => {
  const config = {
    lan: trimOrigin($('lan').value || ''),
    ipv6: trimOrigin($('ipv6').value || ''),
    ssh: trimOrigin($('ssh').value || ''),
  };
  if (!config.lan && !config.ipv6 && !config.ssh) {
    showSetup(config, 'Укажите хотя бы один адрес.');
    return;
  }
  await saveConfig(config);
  await attempt(config);
});

// On launch: if we already have addresses, go straight to probing; otherwise onboard.
(async () => {
  const config = await loadConfig();
  if (config && candidates(config).length > 0) await attempt(config);
  else showSetup(null, null);
})();
