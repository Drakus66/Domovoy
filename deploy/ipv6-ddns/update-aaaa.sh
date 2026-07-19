#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2025-2026 Ilya Dryagin
#
# IPv6 dynamic-DNS updater (mobile-app / remote-access track, Этап 3b). The ISP hands out a global IPv6 that can
# change on lease renewal, so the AAAA record for the home hostname must be kept in sync. This finds the host's
# current global IPv6 and, if it changed, updates the AAAA record via the Cloudflare API (swap the update_dns
# function for your provider). Run it from cron/systemd-timer every few minutes.
#
# Requires: curl, jq, iproute2. Config via env (put them in /etc/domovoy-ddns.env and source it in the timer):
#   CF_API_TOKEN   Cloudflare token with Zone:DNS:Edit on the zone
#   CF_ZONE_ID     the zone id
#   DDNS_HOSTNAME  the FQDN whose AAAA record to update, e.g. home.example.com
set -euo pipefail

: "${DDNS_HOSTNAME:?set DDNS_HOSTNAME (the FQDN to update)}"
STATE_FILE="${STATE_FILE:-/var/lib/domovoy-ddns/last-ipv6}"

# --- discover the current global IPv6 --------------------------------------------------------------------
# Prefer a stable (non-temporary, non-deprecated) global address. Temporary privacy addresses rotate and must
# not be published; 'mngtmpaddr'/'temporary'/'deprecated' are filtered out.
current_ipv6() {
  ip -6 -o addr show scope global 2>/dev/null \
    | grep -viE 'temporary|deprecated|mngtmpaddr' \
    | grep -oE '([0-9a-f]{1,4}:){2,}[0-9a-f]{1,4}' \
    | grep -viE '^fe80|^fc|^fd' \
    | head -n1
}

IP="$(current_ipv6 || true)"
if [ -z "${IP:-}" ]; then
  echo "No global IPv6 found — the ISP may not provide routable IPv6 (CGNAT-over-v6). Falling back to the SSH tunnel is expected here." >&2
  exit 0
fi

# Skip the API call if nothing changed (cheap, avoids rate limits).
mkdir -p "$(dirname "$STATE_FILE")"
if [ -f "$STATE_FILE" ] && [ "$(cat "$STATE_FILE")" = "$IP" ]; then
  exit 0
fi

# --- update the AAAA record (Cloudflare example) ----------------------------------------------------------
update_dns() {
  local ip="$1"
  : "${CF_API_TOKEN:?set CF_API_TOKEN}" "${CF_ZONE_ID:?set CF_ZONE_ID}"

  local record_id
  record_id="$(curl -fsS -H "Authorization: Bearer ${CF_API_TOKEN}" \
    "https://api.cloudflare.com/client/v4/zones/${CF_ZONE_ID}/dns_records?type=AAAA&name=${DDNS_HOSTNAME}" \
    | jq -r '.result[0].id // empty')"

  local payload
  payload="$(jq -nc --arg name "$DDNS_HOSTNAME" --arg content "$ip" \
    '{type:"AAAA", name:$name, content:$content, ttl:120, proxied:false}')"

  if [ -n "$record_id" ]; then
    curl -fsS -X PUT -H "Authorization: Bearer ${CF_API_TOKEN}" -H "Content-Type: application/json" \
      --data "$payload" \
      "https://api.cloudflare.com/client/v4/zones/${CF_ZONE_ID}/dns_records/${record_id}" >/dev/null
  else
    curl -fsS -X POST -H "Authorization: Bearer ${CF_API_TOKEN}" -H "Content-Type: application/json" \
      --data "$payload" \
      "https://api.cloudflare.com/client/v4/zones/${CF_ZONE_ID}/dns_records" >/dev/null
  fi
}

update_dns "$IP"
echo "$IP" > "$STATE_FILE"
echo "Updated AAAA ${DDNS_HOSTNAME} -> ${IP}"
