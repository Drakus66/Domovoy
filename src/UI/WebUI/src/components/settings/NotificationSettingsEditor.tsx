// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, Checkbox, Divider, FormControlLabel, Stack, Switch, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { notificationsApi, NOTIFICATION_CATEGORIES, NotificationSettings } from '../../api/notifications';

/**
 * Notification discipline (roadmap Epic 3F). Per-category channel routing as an opt-out matrix (uncheck a cell to
 * mute that category on that channel), per-category rate-limit (against "cry wolf" fatigue), and the safety floor
 * (a critical alert is always forced onto a prominent channel). Mirrors IntelligenceEditor: self-contained GET/PUT
 * of the notification_settings document. Everything on by default — the user narrows, never widens into surprise.
 */
export default function NotificationSettingsEditor() {
  const { t } = useTranslation('settings');
  const [channels, setChannels] = useState<string[]>([]);
  const [muted, setMuted] = useState<Record<string, string[]>>({});
  const [intervals, setIntervals] = useState<Record<string, number>>({});
  const [safetyFloor, setSafetyFloor] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    notificationsApi.getChannels()
      .then((c) => setChannels(c.all))
      .catch(() => { /* no channels reachable — the matrix just shows none */ });
    notificationsApi.getSettings()
      .then((s) => { setMuted(s.mutedChannels ?? {}); setIntervals(s.minIntervalSeconds ?? {}); setSafetyFloor(s.safetyFloorEnabled); })
      .catch(() => { /* keep defaults; a save creates the document */ });
  }, []);

  const touched = () => setSaved(false);

  const isDelivered = (category: string, channel: string) => !(muted[category] ?? []).includes(channel);

  const toggle = (category: string, channel: string) => {
    setMuted((prev) => {
      const current = prev[category] ?? [];
      const next = current.includes(channel) ? current.filter((c) => c !== channel) : [...current, channel];
      return { ...prev, [category]: next };
    });
    touched();
  };

  const setInterval = (category: string, seconds: number) => {
    setIntervals((prev) => ({ ...prev, [category]: Math.max(0, seconds) }));
    touched();
  };

  const defaultInterval = useMemo<Record<string, number>>(
    () => ({ reactive: 60, proactive: 900, optimization: 3600 }), [],
  );

  const save = async () => {
    setSaving(true); setError(null); setSaved(false);
    try {
      const payload: NotificationSettings = {
        mutedChannels: muted,
        minIntervalSeconds: intervals,
        safetyFloorEnabled: safetyFloor,
      };
      const s = await notificationsApi.saveSettings(payload);
      setMuted(s.mutedChannels ?? {}); setIntervals(s.minIntervalSeconds ?? {}); setSafetyFloor(s.safetyFloorEnabled);
      setSaved(true);
    } catch {
      setError(t('errors.save'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Stack spacing={2}>
      {saved && <Alert severity="success" onClose={() => setSaved(false)}>{t('notifications.saved')}</Alert>}
      {error && <Alert severity="warning" onClose={() => setError(null)}>{error}</Alert>}

      <Typography variant="body2" color="text.secondary">{t('notifications.hint')}</Typography>

      <Box>
        <Typography variant="subtitle2" gutterBottom>{t('notifications.routing')}</Typography>
        {channels.length === 0 ? (
          <Typography variant="caption" color="text.secondary">{t('notifications.noChannels')}</Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t('notifications.category')}</TableCell>
                {channels.map((ch) => <TableCell key={ch} align="center">{ch}</TableCell>)}
              </TableRow>
            </TableHead>
            <TableBody>
              {NOTIFICATION_CATEGORIES.map((cat) => (
                <TableRow key={cat}>
                  <TableCell>{t(`notifications.categories.${cat}`)}</TableCell>
                  {channels.map((ch) => (
                    <TableCell key={ch} align="center" padding="checkbox">
                      <Checkbox size="small" checked={isDelivered(cat, ch)} onChange={() => toggle(cat, ch)} />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Box>

      <Divider />

      <Box>
        <Typography variant="subtitle2" gutterBottom>{t('notifications.rateLimit')}</Typography>
        <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
          {NOTIFICATION_CATEGORIES.map((cat) => (
            <TextField
              key={cat}
              label={t(`notifications.categories.${cat}`)}
              type="number"
              size="small"
              value={intervals[cat] ?? defaultInterval[cat]}
              onChange={(e) => setInterval(cat, Number(e.target.value))}
              inputProps={{ min: 0, max: 86400 }}
              sx={{ width: 160 }}
            />
          ))}
        </Stack>
        <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
          {t('notifications.rateLimitHint')}
        </Typography>
      </Box>

      <Divider />

      <FormControlLabel
        control={<Switch checked={safetyFloor} onChange={(e) => { setSafetyFloor(e.target.checked); touched(); }} />}
        label={t('notifications.safetyFloor')}
      />
      <Typography variant="caption" color="text.secondary" sx={{ mt: -1.5, ml: 6 }}>
        {t('notifications.safetyFloorHint')}
      </Typography>

      <Box>
        <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={save} disabled={saving}>
          {t('notifications.save')}
        </Button>
      </Box>
    </Stack>
  );
}
