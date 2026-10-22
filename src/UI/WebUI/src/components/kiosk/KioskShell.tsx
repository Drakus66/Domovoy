// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ReactNode, useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Box, Button, Dialog, DialogActions, DialogContentText, DialogTitle } from '@mui/material';
import { useKioskStore } from '../../store/kioskStore';

/**
 * Kiosk shell (Epic 2O.2). Wraps the routed content when kiosk mode is on, with no navigation chrome. It:
 *  - pins to the configured screen on entry;
 *  - returns to it after `idleReturnSeconds` of no interaction (so a passer-by leaves it on the home screen);
 *  - dims the screen (a full overlay — the web stand-in for backlight-off) after `dimSeconds`, waking on touch;
 *  - exits only via a deliberately hidden long-press in the top-left corner, behind a confirm.
 *
 * Screen dim here is a visual overlay; true backlight control needs the native Android shell (lock-task + dim).
 */
export default function KioskShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation('kiosk');
  const config = useKioskStore((s) => s.config);
  const exit = useKioskStore((s) => s.exit);
  const navigate = useNavigate();

  const [dim, setDim] = useState(false);
  const [confirmExit, setConfirmExit] = useState(false);

  const idleTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const dimTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const holdTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const resetIdle = useCallback(() => {
    setDim(false);
    if (idleTimer.current) clearTimeout(idleTimer.current);
    if (dimTimer.current) clearTimeout(dimTimer.current);
    if (config.idleReturnSeconds > 0)
      idleTimer.current = setTimeout(() => navigate(config.tabPath), config.idleReturnSeconds * 1000);
    if (config.dimSeconds > 0)
      dimTimer.current = setTimeout(() => setDim(true), config.dimSeconds * 1000);
  }, [config.idleReturnSeconds, config.dimSeconds, config.tabPath, navigate]);

  // Pin to the configured screen once on entry.
  useEffect(() => {
    navigate(config.tabPath);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Track activity → keep resetting the idle/dim timers.
  useEffect(() => {
    const activity = () => resetIdle();
    const events: (keyof WindowEventMap)[] = ['pointerdown', 'keydown', 'wheel', 'touchstart'];
    events.forEach((e) => window.addEventListener(e, activity, { passive: true }));
    resetIdle();
    return () => {
      events.forEach((e) => window.removeEventListener(e, activity));
      if (idleTimer.current) clearTimeout(idleTimer.current);
      if (dimTimer.current) clearTimeout(dimTimer.current);
    };
  }, [resetIdle]);

  const startHold = () => {
    holdTimer.current = setTimeout(() => setConfirmExit(true), 2000);
  };
  const cancelHold = () => {
    if (holdTimer.current) clearTimeout(holdTimer.current);
  };

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default', position: 'relative' }}>
      {children}

      {/* Dim overlay — the full-screen screen-off stand-in; a tap anywhere wakes it. */}
      {dim && (
        <Box
          onClick={resetIdle}
          data-testid="kiosk-dim"
          sx={{ position: 'fixed', inset: 0, bgcolor: 'rgba(0,0,0,0.9)', zIndex: 2000, cursor: 'pointer' }}
        />
      )}

      {/* Hidden exit hotspot — long-press (2s) top-left. Sits above the dim overlay so exit always works. */}
      <Box
        onPointerDown={startHold}
        onPointerUp={cancelHold}
        onPointerLeave={cancelHold}
        aria-hidden
        sx={{ position: 'fixed', top: 0, left: 0, width: 72, height: 72, zIndex: 2100 }}
      />

      <Dialog open={confirmExit} onClose={() => setConfirmExit(false)}>
        <DialogTitle>{t('exitConfirm.title')}</DialogTitle>
        <DialogContentText sx={{ px: 3 }}>{t('exitConfirm.body')}</DialogContentText>
        <DialogActions>
          <Button onClick={() => setConfirmExit(false)}>{t('exitConfirm.cancel')}</Button>
          <Button color="error" onClick={() => { exit(); setConfirmExit(false); }}>
            {t('exitConfirm.confirm')}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
