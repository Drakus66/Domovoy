#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2025-2026 Ilya Dryagin
#
# Domovoy SSH reverse-tunnel entrypoint (Этап 3c). Directly ported from the owner's HA autossh addon
# (github.com/Drakus66/SSH-tunel-HA-addon): generate a restricted ed25519 key on first run, then hold a
# permanent reverse tunnel to the VPS with autossh's reconnect loop. On the VPS, {REMOTE_BIND} forwards to the
# home stack's Caddy edge ({LOCAL_TARGET}, default caddy:443); a reverse proxy there (Caddy/Traefik) publishes it.
set -euo pipefail

SSH_HOST="${SSH_HOST:?set SSH_HOST to your VPS hostname}"
SSH_PORT="${SSH_PORT:-22}"
SSH_USER="${SSH_USER:?set SSH_USER (the restricted tunnel account on the VPS)}"
# What the VPS binds and what it forwards to inside the home stack.
REMOTE_BIND="${REMOTE_BIND:-127.0.0.1:8443}"     # VPS side: bind here, then reverse-proxy to it
LOCAL_TARGET="${LOCAL_TARGET:-caddy:443}"         # home side: the Caddy TLS edge
KEY_PATH="${KEY_PATH:-/keys/domovoy_tunnel_ed25519}"

mkdir -p "$(dirname "$KEY_PATH")"

# First run: create the key and print the *restricted* authorized_keys line to install on the VPS, then exit so
# the operator can add it. `permitopen` + `restrict` limit this key to just this one port-forward (defence in
# depth — a leaked key can't get a shell or open other ports).
if [ ! -f "$KEY_PATH" ]; then
  echo "No tunnel key found — generating one."
  ssh-keygen -t ed25519 -N "" -C "domovoy-autossh" -f "$KEY_PATH"
  echo ""
  echo "==================================================================================="
  echo "Add this line to ~/.ssh/authorized_keys of '${SSH_USER}' on ${SSH_HOST}, then restart:"
  echo ""
  echo "restrict,port-forwarding,permitopen=\"${REMOTE_BIND}\",command=\"\" $(cat "${KEY_PATH}.pub")"
  echo ""
  echo "The VPS sshd also needs 'GatewayPorts clientspecified' (or a reverse proxy in front)."
  echo "==================================================================================="
  exit 1
fi

echo "Starting reverse tunnel: ${SSH_USER}@${SSH_HOST}:${SSH_PORT}  -R ${REMOTE_BIND} -> ${LOCAL_TARGET}"

# -M 0 disables autossh's own monitoring port in favour of SSH keepalives (ServerAliveInterval), exactly as the
# addon does; ExitOnForwardFailure makes a bind clash fail fast so the loop retries cleanly.
while true; do
  autossh -M 0 -N \
    -o ServerAliveInterval=30 \
    -o ServerAliveCountMax=3 \
    -o ExitOnForwardFailure=yes \
    -o StrictHostKeyChecking=accept-new \
    -o UserKnownHostsFile=/keys/known_hosts \
    -i "$KEY_PATH" \
    -p "$SSH_PORT" \
    -R "${REMOTE_BIND}:${LOCAL_TARGET}" \
    "${SSH_USER}@${SSH_HOST}" || true
  echo "Tunnel dropped — reconnecting in 5s…"
  sleep 5
done
