// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert, Box, Button, Card, CardContent, FormControlLabel, Link, Stack, Switch, Typography,
} from '@mui/material';
import { mlApi, MlSettings } from '../../api/ml';

/**
 * The intelligence-layer switches on the ML page (roadmap Epic 3I): pause the whole layer or just the proactive
 * suggestions, without a restart. Lives on the Tasks tab (it gates training) — a prominent alert when the layer
 * is off explains why nothing is learning. Self-contained: fetches its own settings, optimistic on toggle.
 */
export default function MlLayerControls() {
  const { t } = useTranslation('models');
  const [settings, setSettings] = useState<MlSettings | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    mlApi.getSettings().then(setSettings).catch(() => undefined);
  }, []);

  const save = async (next: MlSettings) => {
    setSettings(next); // optimistic
    setSaving(true);
    try {
      const saved = await mlApi.saveSettings({
        enabled: next.enabled,
        proposalsEnabled: next.proposalsEnabled,
        minHistoryDays: next.minHistoryDays,
      });
      setSettings(saved);
    } catch {
      mlApi.getSettings().then(setSettings).catch(() => undefined); // re-read the truth on failure
    } finally {
      setSaving(false);
    }
  };

  if (!settings) return null;

  return (
    <Stack spacing={2} mb={2}>
      {!settings.enabled && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" disabled={saving} onClick={() => save({ ...settings, enabled: true })}>
              {t('layer.enable')}
            </Button>
          }
        >
          {t('layer.disabledAlert')}
        </Alert>
      )}

      <Card variant="outlined">
        <CardContent sx={{ '&:last-child': { pb: 2 } }}>
          <Stack direction="row" alignItems="center" justifyContent="space-between" flexWrap="wrap" useFlexGap>
            <Box>
              <Typography variant="subtitle2">{t('layer.title')}</Typography>
              <Typography variant="caption" color="text.secondary">{t('layer.caption')}</Typography>
            </Box>
            <Stack direction="row" spacing={2} alignItems="center">
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={settings.enabled}
                    disabled={saving}
                    onChange={(e) => save({ ...settings, enabled: e.target.checked })}
                  />
                }
                label={t('layer.enabled')}
              />
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={settings.enabled && settings.proposalsEnabled}
                    disabled={saving || !settings.enabled}
                    onChange={(e) => save({ ...settings, proposalsEnabled: e.target.checked })}
                  />
                }
                label={t('layer.proposals')}
              />
              <Link component={RouterLink} to="/settings" variant="body2" underline="hover">
                {t('layer.settingsLink')}
              </Link>
            </Stack>
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  );
}
