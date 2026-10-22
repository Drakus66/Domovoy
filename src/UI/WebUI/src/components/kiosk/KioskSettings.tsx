// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, MenuItem, Stack, TextField } from '@mui/material';
import TabletMacRoundedIcon from '@mui/icons-material/TabletMacRounded';
import { dashboardsApi, type Dashboard } from '../../api/dashboards';
import { DEFAULT_KIOSK_CONFIG, useKioskStore } from '../../store/kioskStore';

/**
 * Kiosk configuration (Epic 2O.2), rendered on the Settings page. Picks the pinned screen (home or a custom
 * dashboard), the idle-return and dim timeouts, and enters kiosk mode. Exiting is the hidden long-press in the
 * running shell, so there is intentionally no "exit" button here.
 */
export default function KioskSettings() {
  const { t } = useTranslation('kiosk');
  const enter = useKioskStore((s) => s.enter);

  const [dashboards, setDashboards] = useState<Dashboard[]>([]);
  const [tabPath, setTabPath] = useState(DEFAULT_KIOSK_CONFIG.tabPath);
  const [idleReturn, setIdleReturn] = useState(DEFAULT_KIOSK_CONFIG.idleReturnSeconds);
  const [dimSeconds, setDimSeconds] = useState(DEFAULT_KIOSK_CONFIG.dimSeconds);

  useEffect(() => {
    dashboardsApi.list().then(setDashboards).catch(() => setDashboards([]));
  }, []);

  const num = (value: string, fallback: number) => {
    const n = Number(value);
    return Number.isFinite(n) && n >= 0 ? Math.floor(n) : fallback;
  };

  return (
    <Stack spacing={2}>
      <TextField
        select
        label={t('screen')}
        value={tabPath}
        onChange={(e) => setTabPath(e.target.value)}
        fullWidth
      >
        <MenuItem value="/">{t('home')}</MenuItem>
        {dashboards.map((d) => (
          <MenuItem key={d.id} value={`/t/${d.id}`}>{d.name}</MenuItem>
        ))}
      </TextField>

      <TextField
        type="number"
        label={t('idleReturn')}
        helperText={t('idleReturnHelp')}
        value={idleReturn}
        onChange={(e) => setIdleReturn(num(e.target.value, DEFAULT_KIOSK_CONFIG.idleReturnSeconds))}
        inputProps={{ min: 0 }}
        fullWidth
      />

      <TextField
        type="number"
        label={t('dim')}
        helperText={t('dimHelp')}
        value={dimSeconds}
        onChange={(e) => setDimSeconds(num(e.target.value, DEFAULT_KIOSK_CONFIG.dimSeconds))}
        inputProps={{ min: 0 }}
        fullWidth
      />

      <Alert severity="info">{t('exitHint')}</Alert>

      <Button
        variant="contained"
        startIcon={<TabletMacRoundedIcon />}
        onClick={() => enter({ tabPath, idleReturnSeconds: idleReturn, dimSeconds })}
      >
        {t('enter')}
      </Button>
    </Stack>
  );
}
